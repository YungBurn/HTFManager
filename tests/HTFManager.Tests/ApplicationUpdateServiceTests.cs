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
            return JsonResponse(ReleaseJson("v0.3.9", Array.Empty<object>()));
        });
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.9", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.UpToDate, update.State);
    }

    [Fact]
    public async Task CheckAsync_DoesNotOfferDowngradeWhenLatestReleaseIsOlder()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Contains("/releases/latest", request.RequestUri!.AbsoluteUri);
            return JsonResponse(ReleaseJson("v0.3.8", Array.Empty<object>()));
        });
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.9", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.UpToDate, update.State);
        Assert.Equal("0.3.8", update.LatestVersion);
    }

    [Fact]
    public async Task CheckAsync_RejectsManifestVersionThatDoesNotMatchReleaseTag()
    {
        var bytes = "new-executable"u8.ToArray();
        var handler = BuildHandler("0.3.9", bytes, mutateManifest: manifest => manifest.Version = "0.4.0");
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, update.State);
        Assert.Contains("version", update.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_RejectsMalformedManifestSha256()
    {
        var bytes = "new-executable"u8.ToArray();
        var handler = BuildHandler("0.3.9", bytes, mutateManifest: manifest => manifest.Sha256 = "not-a-sha256");
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, update.State);
        Assert.Contains("SHA-256", update.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_RejectsUnexpectedExecutableAssetName()
    {
        var bytes = "new-executable"u8.ToArray();
        var handler = BuildHandler("0.3.9", bytes, mutateManifest: manifest => manifest.Asset = "Other.exe");
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, update.State);
        Assert.Contains("HTFManager.exe", update.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_RejectsNonHttpsExecutableAssetUrl()
    {
        var bytes = "new-executable"u8.ToArray();
        var handler = BuildHandler("0.3.9", bytes, executableUrl: "http://download.invalid/HTFManager.exe");
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));

        var update = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, update.State);
        Assert.Contains("HTTPS", update.Error ?? "", StringComparison.OrdinalIgnoreCase);
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
        var advertised = "EXPECTED"u8.ToArray();
        var downloaded = "TAMPERED"u8.ToArray();
        var handler = BuildHandler("0.3.9", advertised, downloaded);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var failed = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, failed.State);
        Assert.Contains("SHA-256", failed.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_RejectsContentLengthMismatchBeforeStaging()
    {
        var advertised = "expected"u8.ToArray();
        var downloaded = "different-length"u8.ToArray();
        var handler = BuildHandler("0.3.9", advertised, downloaded);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var failed = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, failed.State);
        Assert.Contains("Content-Length", failed.Error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_root, "updates", "0.3.9", "HTFManager.exe")));
    }

    [Fact]
    public async Task DownloadAsync_RejectsBodyThatExceedsManifestSizeWithoutContentLength()
    {
        var advertised = "expected"u8.ToArray();
        var downloaded = "expected-plus-extra"u8.ToArray();
        var handler = BuildHandler("0.3.9", advertised, downloaded, suppressDownloadContentLength: true);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var failed = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, failed.State);
        Assert.Contains("exceeded", failed.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_RemovesPartialDownloadAfterVerificationFailure()
    {
        var advertised = "EXPECTED"u8.ToArray();
        var downloaded = "TAMPERED"u8.ToArray();
        var handler = BuildHandler("0.3.9", advertised, downloaded);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var failed = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Error, failed.State);
        var versionDirectory = Path.Combine(_root, "updates", "0.3.9");
        Assert.Empty(Directory.Exists(versionDirectory)
            ? Directory.EnumerateFiles(versionDirectory, "*.download-*", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>());
        Assert.False(File.Exists(Path.Combine(versionDirectory, "HTFManager.exe")));
    }

    [Fact]
    public async Task DownloadAsync_ReusesAlreadyVerifiedStagedExecutable()
    {
        var bytes = "new-executable"u8.ToArray();
        var executableRequests = 0;
        var handler = BuildHandler("0.3.9", bytes, onExecutableRequest: () => executableRequests++);
        var service = new GitHubReleaseUpdateService(_root, new HttpClient(handler));
        var available = await service.CheckAsync("0.3.8", TestContext.Current.CancellationToken);

        var first = await service.DownloadAsync(available, TestContext.Current.CancellationToken);
        var second = await service.DownloadAsync(available, TestContext.Current.CancellationToken);

        Assert.Equal(ApplicationUpdateState.Ready, first.State);
        Assert.Equal(ApplicationUpdateState.Ready, second.State);
        Assert.Equal(first.StagedPath, second.StagedPath);
        Assert.Equal(1, executableRequests);
    }

    private static FakeHandler BuildHandler(
        string version,
        byte[] manifestBytes,
        byte[]? downloadBytes = null,
        Action<ApplicationUpdateManifest>? mutateManifest = null,
        string executableUrl = "https://download.invalid/HTFManager.exe",
        bool suppressDownloadContentLength = false,
        Action? onExecutableRequest = null)
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
        mutateManifest?.Invoke(manifest);

        var assets = new object[]
        {
            new { name = "update-manifest.json", browser_download_url = "https://download.invalid/update-manifest.json", size = 200L },
            new { name = assetName, browser_download_url = executableUrl, size = (long)manifestBytes.Length }
        };

        return new FakeHandler(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri.Contains("/releases/latest", StringComparison.Ordinal))
                return JsonResponse(ReleaseJson("v" + version, assets));
            if (uri.EndsWith("update-manifest.json", StringComparison.Ordinal))
                return JsonResponse(JsonSerializer.Serialize(manifest));
            if (uri.EndsWith("HTFManager.exe", StringComparison.Ordinal))
            {
                onExecutableRequest?.Invoke();
                var content = new ByteArrayContent(downloadBytes ?? manifestBytes);
                if (suppressDownloadContentLength)
                    content.Headers.ContentLength = null;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
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
