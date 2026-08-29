using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Updates;

public sealed class GitHubReleaseUpdateService : IApplicationUpdateService
{
    private const string Owner = "YungBurn";
    private const string Repository = "HTFManager";
    private const string ManifestAssetName = "update-manifest.json";
    private const string ExecutableAssetName = "HTFManager.exe";
    private readonly string _updatesDirectory;
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateService(string dataDirectory, HttpClient? httpClient = null)
    {
        _updatesDirectory = Path.Combine(dataDirectory, "updates");
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HTFManager", "0.3.9"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        CleanupStaleDownloadFragments();
    }

    public async Task<ApplicationUpdateInfo> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var normalizedCurrent = NormalizeVersion(currentVersion);
        try
        {
            CleanupStaleDownloadFragments();
            using var response = await _httpClient.GetAsync(
                $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(responseStream, cancellationToken: cancellationToken)
                          ?? throw new InvalidDataException("GitHub returned an empty release response.");
            var latest = NormalizeVersion(release.TagName);
            if (!TryCompareVersions(latest, normalizedCurrent, out var comparison))
                throw new InvalidDataException($"Release version '{release.TagName}' is not a supported application version.");

            if (comparison <= 0)
            {
                return new ApplicationUpdateInfo
                {
                    State = ApplicationUpdateState.UpToDate,
                    CurrentVersion = normalizedCurrent,
                    LatestVersion = latest,
                    ReleaseName = release.Name,
                    ReleaseNotes = release.Body,
                    ReleasePageUrl = release.HtmlUrl,
                    PublishedAt = release.PublishedAt
                };
            }

            var manifestAsset = release.Assets.FirstOrDefault(asset =>
                asset.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is null)
                return Error(normalizedCurrent, latest, release, "The latest release does not contain update-manifest.json.");
            if (!IsHttpsUrl(manifestAsset.BrowserDownloadUrl))
                return Error(normalizedCurrent, latest, release, "The update manifest download URL is invalid or not HTTPS.");

            var manifest = await DownloadManifestAsync(manifestAsset.BrowserDownloadUrl, cancellationToken);
            var manifestError = ValidateManifest(manifest, latest, release.Assets, out var executableAsset);
            if (manifestError is not null || executableAsset is null)
                return Error(normalizedCurrent, latest, release, manifestError ?? "The release executable asset is unavailable.");

            return new ApplicationUpdateInfo
            {
                State = ApplicationUpdateState.Available,
                CurrentVersion = normalizedCurrent,
                LatestVersion = latest,
                ReleaseName = release.Name,
                ReleaseNotes = release.Body,
                ReleasePageUrl = release.HtmlUrl,
                PublishedAt = release.PublishedAt,
                Manifest = manifest,
                AssetDownloadUrl = executableAsset.BrowserDownloadUrl
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ApplicationUpdateInfo
            {
                State = ApplicationUpdateState.Error,
                CurrentVersion = normalizedCurrent,
                Error = ex.Message
            };
        }
    }

    public async Task<ApplicationUpdateInfo> DownloadAsync(ApplicationUpdateInfo update, CancellationToken cancellationToken = default)
    {
        var metadataError = ValidateDownloadRequest(update);
        if (metadataError is not null)
            return CloneError(update, metadataError);

        var manifest = update.Manifest!;
        var versionDirectory = Path.Combine(_updatesDirectory, SafeSegment(update.LatestVersion!));
        var finalPath = Path.Combine(versionDirectory, ExecutableAssetName);
        var tempPath = finalPath + ".download-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(versionDirectory);
            CleanupDownloadFragments(versionDirectory);

            if (await IsValidStagedFileAsync(finalPath, manifest, cancellationToken))
                return Ready(update, finalPath);

            TryDelete(finalPath);

            using var response = await _httpClient.GetAsync(update.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength >= 0 && contentLength != manifest.Size)
                throw new InvalidDataException("Downloaded update Content-Length does not match the release manifest.");

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await CopyWithSizeLimitAsync(input, output, manifest.Size, cancellationToken);

            var info = new FileInfo(tempPath);
            if (info.Length != manifest.Size)
                throw new InvalidDataException("Downloaded update size does not match the release manifest.");

            var actualHash = await ComputeSha256Async(tempPath, cancellationToken);
            if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded update failed SHA-256 verification.");

            File.Move(tempPath, finalPath, true);
            return Ready(update, finalPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(finalPath);
            return CloneError(update, ex.Message);
        }
        finally
        {
            // Cancellation is intentionally allowed to propagate, but partial downloads must never remain usable.
            TryDelete(tempPath);
        }
    }

    private async Task<ApplicationUpdateManifest> DownloadManifestAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<ApplicationUpdateManifest>(stream, cancellationToken: cancellationToken)
               ?? throw new InvalidDataException("Update manifest is empty.");
    }

    private static string? ValidateManifest(
        ApplicationUpdateManifest manifest,
        string latestVersion,
        IReadOnlyList<GitHubAsset> assets,
        out GitHubAsset? executableAsset)
    {
        executableAsset = null;
        if (manifest.SchemaVersion != 1) return "Unsupported update manifest schema.";
        if (!string.Equals(manifest.Channel, "stable", StringComparison.OrdinalIgnoreCase)) return "The latest release is not on the stable update channel.";
        if (!string.Equals(manifest.Rid, "win-x64", StringComparison.OrdinalIgnoreCase)) return "The latest release does not target win-x64.";
        if (!Version.TryParse(NormalizeVersion(manifest.Version), out _)) return "Update manifest contains an invalid application version.";
        if (!NormalizeVersion(manifest.Version).Equals(latestVersion, StringComparison.OrdinalIgnoreCase)) return "Update manifest version does not match the GitHub release tag.";
        if (!string.Equals(manifest.Asset, ExecutableAssetName, StringComparison.OrdinalIgnoreCase)) return $"Update manifest must reference {ExecutableAssetName}.";
        if (Path.GetFileName(manifest.Asset) != manifest.Asset) return "Update manifest contains an invalid asset name.";
        if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) return "Update manifest contains an invalid SHA-256 value.";
        if (manifest.Size <= 0) return "Update manifest contains an invalid asset size.";

        executableAsset = assets.FirstOrDefault(asset => asset.Name.Equals(manifest.Asset, StringComparison.OrdinalIgnoreCase));
        if (executableAsset is null) return "Update manifest references a missing release asset.";
        if (!IsHttpsUrl(executableAsset.BrowserDownloadUrl)) return "The executable download URL is invalid or not HTTPS.";
        if (executableAsset.Size > 0 && executableAsset.Size != manifest.Size) return "Update manifest size does not match the GitHub release asset.";
        return null;
    }

    private static string? ValidateDownloadRequest(ApplicationUpdateInfo update)
    {
        if (update.Manifest is null || string.IsNullOrWhiteSpace(update.AssetDownloadUrl) || string.IsNullOrWhiteSpace(update.LatestVersion))
            return "Update metadata is incomplete.";
        if (update.State is not (ApplicationUpdateState.Available or ApplicationUpdateState.Downloading or ApplicationUpdateState.Ready))
            return "The update is not in a downloadable state.";
        if (!string.Equals(update.Manifest.Asset, ExecutableAssetName, StringComparison.OrdinalIgnoreCase))
            return $"Update metadata does not reference {ExecutableAssetName}.";
        if (update.Manifest.Size <= 0)
            return "Update metadata contains an invalid executable size.";
        if (string.IsNullOrWhiteSpace(update.Manifest.Sha256) || update.Manifest.Sha256.Length != 64 || !update.Manifest.Sha256.All(Uri.IsHexDigit))
            return "Update metadata contains an invalid SHA-256 value.";
        if (!IsHttpsUrl(update.AssetDownloadUrl))
            return "The executable download URL is invalid or not HTTPS.";
        return null;
    }

    private static async Task CopyWithSizeLimitAsync(Stream input, Stream output, long expectedSize, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > expectedSize)
                throw new InvalidDataException("Downloaded update exceeded the size declared by the release manifest.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<bool> IsValidStagedFileAsync(
        string path,
        ApplicationUpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length != manifest.Size) return false;
            var hash = await ComputeSha256Async(path, cancellationToken);
            return hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static ApplicationUpdateInfo Ready(ApplicationUpdateInfo update, string stagedPath)
        => new()
        {
            State = ApplicationUpdateState.Ready,
            CurrentVersion = update.CurrentVersion,
            LatestVersion = update.LatestVersion,
            ReleaseName = update.ReleaseName,
            ReleaseNotes = update.ReleaseNotes,
            ReleasePageUrl = update.ReleasePageUrl,
            PublishedAt = update.PublishedAt,
            Manifest = update.Manifest,
            AssetDownloadUrl = update.AssetDownloadUrl,
            StagedPath = stagedPath
        };

    private static ApplicationUpdateInfo Error(string current, string latest, GitHubRelease release, string error)
        => new()
        {
            State = ApplicationUpdateState.Error,
            CurrentVersion = current,
            LatestVersion = latest,
            ReleaseName = release.Name,
            ReleaseNotes = release.Body,
            ReleasePageUrl = release.HtmlUrl,
            PublishedAt = release.PublishedAt,
            Error = error
        };

    private static ApplicationUpdateInfo CloneError(ApplicationUpdateInfo update, string error)
        => new()
        {
            State = ApplicationUpdateState.Error,
            CurrentVersion = update.CurrentVersion,
            LatestVersion = update.LatestVersion,
            ReleaseName = update.ReleaseName,
            ReleaseNotes = update.ReleaseNotes,
            ReleasePageUrl = update.ReleasePageUrl,
            PublishedAt = update.PublishedAt,
            Manifest = update.Manifest,
            AssetDownloadUrl = update.AssetDownloadUrl,
            Error = error
        };

    private static bool TryCompareVersions(string left, string right, out int comparison)
    {
        comparison = 0;
        if (!Version.TryParse(left, out var leftVersion) || !Version.TryParse(right, out var rightVersion)) return false;
        comparison = leftVersion.CompareTo(rightVersion);
        return true;
    }

    private static string NormalizeVersion(string? value)
    {
        var normalized = (value ?? "0.0.0").Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        return normalized;
    }

    private static string SafeSegment(string value)
        => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static bool IsHttpsUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void CleanupStaleDownloadFragments()
    {
        try
        {
            if (!Directory.Exists(_updatesDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(_updatesDirectory, "*.download-*", SearchOption.AllDirectories))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromHours(1))
                        File.Delete(path);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void CleanupDownloadFragments(string directory)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.download-*", SearchOption.TopDirectoryOnly))
                TryDelete(path);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
