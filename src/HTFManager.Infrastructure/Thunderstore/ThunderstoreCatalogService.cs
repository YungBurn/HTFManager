using System.Net.Http.Headers;
using System.Text.Json;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Thunderstore;

public sealed class ThunderstoreCatalogService : IModCatalogService, IDisposable
{
    public const string CommunityIdentifier = "how-to-fish";
    private static readonly Uri PackageIndexUri = new($"https://thunderstore.io/c/{CommunityIdentifier}/api/v1/package/");
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _indexCachePath;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ThunderstoreCatalogService(string dataDirectory)
    {
        _cacheDirectory = Path.Combine(dataDirectory, "cache", "thunderstore");
        _indexCachePath = Path.Combine(_cacheDirectory, $"{CommunityIdentifier}-packages.json");
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HTFManager", "0.2.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<RemoteModPackage>> GetPackagesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && IsFreshCache())
        {
            var cached = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            if (cached.Count > 0) return cached;
        }

        try
        {
            using var response = await _httpClient.GetAsync(PackageIndexUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var packages = Deserialize(json);

            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(_indexCachePath, json, cancellationToken).ConfigureAwait(false);
            return packages;
        }
        catch
        {
            var fallback = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            if (fallback.Count > 0) return fallback;
            throw;
        }
    }

    public async Task<string> DownloadPackageAsync(RemoteModPackage package, RemoteModVersion version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version.DownloadUrl))
            throw new InvalidOperationException("Thunderstore did not provide a download URL for this version.");

        var downloads = Path.Combine(_cacheDirectory, "downloads");
        Directory.CreateDirectory(downloads);
        var safe = SanitizeFileName($"{package.Owner}-{package.Name}-{version.VersionNumber}.zip");
        var destination = Path.Combine(downloads, safe);
        var temp = destination + ".part";

        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            return destination;

        using var response = await _httpClient.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(temp))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, destination, true);
        return destination;
    }

    private bool IsFreshCache()
    {
        try
        {
            if (!File.Exists(_indexCachePath)) return false;
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(_indexCachePath) < _cacheLifetime;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<RemoteModPackage>> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_indexCachePath)) return Array.Empty<RemoteModPackage>();
            var json = await File.ReadAllTextAsync(_indexCachePath, cancellationToken).ConfigureAwait(false);
            return Deserialize(json);
        }
        catch
        {
            return Array.Empty<RemoteModPackage>();
        }
    }

    private static IReadOnlyList<RemoteModPackage> Deserialize(string json)
        => JsonSerializer.Deserialize<List<RemoteModPackage>>(json, JsonOptions)
           ?? new List<RemoteModPackage>();

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public void Dispose() => _httpClient.Dispose();
}
