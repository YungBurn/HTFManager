using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Updates;

namespace HTFManager.Tests;

public sealed class ApplicationUpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));

    public ApplicationUpdateServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CheckAsync_ReturnsAvailableOnlyWithMatchingStableManifest()
    {
        var bytes = "new-executable"u8.ToArray();
        var handler = BuildHandler("0.3.9", bytes);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Available, update.State);
        Assert.Equal("0.3.9", update.LatestVersion);
        Assert.NotNull(update.Manifest);
        Assert.Equal("HTFManager.exe", update.Manifest!.Asset);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUpToDateWithoutRequiringManifestForSameVersion()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Contains("/releases/latest", request.RequestUri!.AbsoluteUri);
            return JsonResponse(ReleaseJson("v0.3.8", Array.Empty<object>()));
        });
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.UpToDate, update.State);
    }

    [Fact]
    public async Task DownloadAsync_VerifiesSha256AndStagesExecutable()
    {
        var bytes = "new-executable"u8.ToArray();
        var handler = BuildHandler("0.3.9", bytes);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var ready = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Ready, ready.State);
        Assert.True(File.Exists(ready.StagedPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ready.StagedPath!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_RejectsChangedExecutableContent()
    {
        var advertised = "expected"u8.ToArray();
        var downloaded = "tampered"u8.ToArray();
        var handler = BuildHandler("0.3.9", advertised, downloaded);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var failed = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, failed.State);
        Assert.Contains("SHA-256", failed.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static FakeHandler BuildHandler(string version, byte[] manifestBytes, byte[]? downloadBytes = null)
    {
        var assetName = "HTFManager.exe";
        var hash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        var manifest = new ApplicationUpdateManifest
        {
            SchemaVersion = 1,
            Channel = "stable",
            Version = version,
            Rid = "win-x64",
            Asset = assetName,
            Size = manifestBytes.Length,
            Sha256 = hash,
            PublishedAt = DateTimeOffset.UtcNow
        };
        var assets = new object[]
        {
            new { name = "update-manifest.json", browser_download_url = "https://download.invalid/update-manifest.json", size = 200L },
            new { name = assetName, browser_download_url = "https://download.invalid/HTFManager.exe", size = (long)manifestBytes.Length }
        };

        return new FakeHandler(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri.Contains("/releases/latest", StringComparison.Ordinal))
                return JsonResponse(ReleaseJson("v" + version, assets));
            if (uri.EndsWith("update-manifest.json", StringComparison.Ordinal))
                return JsonResponse(JsonSerializer.Serialize(manifest));
            if (uri.EndsWith("HTFManager.exe", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(downloadBytes ?? manifestBytes)
                };
            throw new InvalidOperationException("Unexpected request: " + uri);
        });
    }

    private static string ReleaseJson(string tag, IEnumerable<object> assets)
        => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            name = "HTF Manager " + tag,
            body = "notes",
            html_url = "https://github.invalid/release",
            published_at = DateTimeOffset.UtcNow,
            assets
        });

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
