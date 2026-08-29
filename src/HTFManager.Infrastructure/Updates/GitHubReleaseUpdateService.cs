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
    private readonly string _updatesDirectory;
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateService(string dataDirectory, HttpClient? httpClient = null)
    {
        _updatesDirectory = Path.Combine(dataDirectory, "updates");
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HTFManager", "0.3.8"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ApplicationUpdateInfo> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var normalizedCurrent = NormalizeVersion(currentVersion);
        try
        {
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
        if (update.Manifest is null || string.IsNullOrWhiteSpace(update.AssetDownloadUrl) || string.IsNullOrWhiteSpace(update.LatestVersion))
            return CloneError(update, "Update metadata is incomplete.");

        var versionDirectory = Path.Combine(_updatesDirectory, SafeSegment(update.LatestVersion));
        var finalPath = Path.Combine(versionDirectory, Path.GetFileName(update.Manifest.Asset));
        var tempPath = finalPath + ".download-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(versionDirectory);
            using var response = await _httpClient.GetAsync(update.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await input.CopyToAsync(output, cancellationToken);

            var info = new FileInfo(tempPath);
            if (update.Manifest.Size > 0 && info.Length != update.Manifest.Size)
                throw new InvalidDataException("Downloaded update size does not match the release manifest.");

            var actualHash = await ComputeSha256Async(tempPath, cancellationToken);
            if (!actualHash.Equals(update.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded update failed SHA-256 verification.");

            File.Move(tempPath, finalPath, true);
            return new ApplicationUpdateInfo
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
                StagedPath = finalPath
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(tempPath);
            return CloneError(update, ex.Message);
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
        if (!manifest.Channel.Equals("stable", StringComparison.OrdinalIgnoreCase)) return "The latest release is not on the stable update channel.";
        if (!manifest.Rid.Equals("win-x64", StringComparison.OrdinalIgnoreCase)) return "The latest release does not target win-x64.";
        if (!NormalizeVersion(manifest.Version).Equals(latestVersion, StringComparison.OrdinalIgnoreCase)) return "Update manifest version does not match the GitHub release tag.";
        if (string.IsNullOrWhiteSpace(manifest.Asset) || Path.GetFileName(manifest.Asset) != manifest.Asset) return "Update manifest contains an invalid asset name.";
        if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) return "Update manifest contains an invalid SHA-256 value.";
        if (manifest.Size <= 0) return "Update manifest contains an invalid asset size.";

        executableAsset = assets.FirstOrDefault(asset => asset.Name.Equals(manifest.Asset, StringComparison.OrdinalIgnoreCase));
        if (executableAsset is null) return "Update manifest references a missing release asset.";
        if (executableAsset.Size > 0 && executableAsset.Size != manifest.Size) return "Update manifest size does not match the GitHub release asset.";
        return null;
    }

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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
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
