using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Infrastructure.Loaders;

public sealed class LoaderSetupService : ILoaderSetupService, IDisposable
{
    public const string RecommendedBepInExVersion = "5.4.2305";
    public const string RecommendedMelonLoaderVersion = "0.7.3";

    private const string BepInExDownloadUrl = "https://thunderstore.io/package/download/BepInEx/BepInExPack/5.4.2305/";
    private const string BepInExSourceUrl = "https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/";
    private const string MelonSourceUrl = "https://github.com/LavaGang/MelonLoader/releases";
    private const string MelonReleaseApi = "https://api.github.com/repos/LavaGang/MelonLoader/releases/latest";

    private readonly LoaderRegistryStore _registry;
    private readonly string _dataDirectory;
    private readonly HttpClient _httpClient;

    public LoaderSetupService(LoaderRegistryStore registry, string dataDirectory)
    {
        _registry = registry;
        _dataDirectory = dataDirectory;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HTFManager", "0.3.2"));
    }

    public LoaderRecommendation GetRecommendation(ModLoaderKind loader)
        => loader switch
        {
            ModLoaderKind.BepInEx => new LoaderRecommendation
            {
                Loader = loader,
                Version = RecommendedBepInExVersion,
                SourceName = "Thunderstore / BepInEx",
                SourceUrl = BepInExSourceUrl,
                DownloadUrl = BepInExDownloadUrl
            },
            ModLoaderKind.MelonLoader => new LoaderRecommendation
            {
                Loader = loader,
                Version = RecommendedMelonLoaderVersion,
                SourceName = "LavaGang / GitHub",
                SourceUrl = MelonSourceUrl,
                DownloadUrl = $"https://github.com/LavaGang/MelonLoader/releases/download/v{RecommendedMelonLoaderVersion}/MelonLoader.x64.zip"
            },
            _ => new LoaderRecommendation { Loader = loader }
        };

    public LoaderInstallRecord? GetManagedRecord(ModLoaderKind loader, string? gameDirectory)
        => string.IsNullOrWhiteSpace(gameDirectory) ? null : _registry.Find(loader, gameDirectory);

    public async Task<LoaderOperationResult> InstallOrRepairAsync(
        ModLoaderKind loader,
        GameEnvironmentInfo environment,
        bool keepPackageCache = true,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return LoaderOperationResult.Fail("Automatic loader setup is currently supported on Windows only.");
        if (!environment.GameFound || string.IsNullOrWhiteSpace(environment.GameDirectory))
            return LoaderOperationResult.Fail("Game directory is not available.");
        if (loader is not (ModLoaderKind.BepInEx or ModLoaderKind.MelonLoader))
            return LoaderOperationResult.Fail("Unsupported mod loader.");

        var gameDirectory = environment.GameDirectory;
        var recommendation = GetRecommendation(loader);
        var transactionRoot = Path.Combine(_dataDirectory, "staging", "loader-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(transactionRoot, "loader.zip");
        var extractRoot = Path.Combine(transactionRoot, "extract");
        var backupRoot = Path.Combine(transactionRoot, "backup");
        var installedTargets = new List<string>();
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(transactionRoot);
            Directory.CreateDirectory(extractRoot);
            Directory.CreateDirectory(backupRoot);

            var download = loader == ModLoaderKind.MelonLoader
                ? await ResolveMelonDownloadAsync(cancellationToken).ConfigureAwait(false)
                : (recommendation.DownloadUrl, recommendation.Version);

            await DownloadAsync(download.Item1, archivePath, cancellationToken).ConfigureAwait(false);
            SafeExtractZip(archivePath, extractRoot);

            var contentRoot = loader == ModLoaderKind.BepInEx
                ? FindBepInExContentRoot(extractRoot)
                : FindMelonContentRoot(extractRoot);
            if (contentRoot is null)
                return LoaderOperationResult.Fail("The downloaded loader archive did not contain the expected Windows x64 layout.");

            var relativeFiles = EnumerateLoaderFiles(loader, contentRoot).ToArray();
            if (relativeFiles.Length == 0)
                return LoaderOperationResult.Fail("No loader files were found in the downloaded archive.");

            var existingRecord = _registry.Find(loader, gameDirectory);
            var managedFiles = existingRecord?.Files.ToHashSet(StringComparer.OrdinalIgnoreCase)
                               ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var relative in relativeFiles)
            {
                var target = SafeCombine(gameDirectory, relative);
                if (!File.Exists(target)) continue;
                if (!managedFiles.Contains(NormalizeRelative(relative)))
                    return LoaderOperationResult.Fail($"A non-managed file already exists at {relative}. HTF Manager will not overwrite it automatically.");
            }

            foreach (var relative in relativeFiles)
            {
                var source = SafeCombine(contentRoot, relative);
                var target = SafeCombine(gameDirectory, relative);
                if (File.Exists(target)) BackupExisting(target, backupRoot, backups);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
                installedTargets.Add(target);
            }

            var obsolete = existingRecord?.Files
                .Where(old => !relativeFiles.Contains(NormalizeRelative(old), StringComparer.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<string>();
            foreach (var relative in obsolete)
            {
                var target = SafeCombine(gameDirectory, relative);
                if (File.Exists(target)) BackupExisting(target, backupRoot, backups);
                if (File.Exists(target)) File.Delete(target);
            }

            EnsureLoaderDirectories(loader, gameDirectory);
            var record = new LoaderInstallRecord
            {
                Loader = loader,
                GameDirectory = gameDirectory,
                Version = download.Item2,
                SourceName = recommendation.SourceName,
                SourceUrl = recommendation.SourceUrl,
                InstalledAt = DateTimeOffset.UtcNow,
                Files = relativeFiles.Select(NormalizeRelative).OrderBy(x => x).ToList()
            };
            _registry.Save(record);

            if (keepPackageCache)
            {
                var cache = Path.Combine(_dataDirectory, "cache", "loaders");
                Directory.CreateDirectory(cache);
                File.Copy(archivePath, Path.Combine(cache, $"{loader}-{record.Version}.zip"), true);
            }

            return LoaderOperationResult.Ok(existingRecord is null ? "Loader installed." : "Loader repaired/updated.", record);
        }
        catch (Exception ex)
        {
            try
            {
                foreach (var target in installedTargets)
                    if (File.Exists(target)) File.Delete(target);
                foreach (var backup in backups)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup.Key)!);
                    File.Copy(backup.Value, backup.Key, true);
                }
            }
            catch { }
            return LoaderOperationResult.Fail(ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(transactionRoot)) Directory.Delete(transactionRoot, true); } catch { }
        }
    }

    public LoaderOperationResult Uninstall(ModLoaderKind loader, GameEnvironmentInfo environment)
    {
        if (string.IsNullOrWhiteSpace(environment.GameDirectory))
            return LoaderOperationResult.Fail("Game directory is not available.");
        var record = _registry.Find(loader, environment.GameDirectory);
        if (record is null)
            return LoaderOperationResult.Fail("This loader installation is not managed by HTF Manager.");

        try
        {
            foreach (var relative in record.Files.OrderByDescending(x => x.Length))
            {
                var target = SafeCombine(environment.GameDirectory, relative);
                if (File.Exists(target)) File.Delete(target);
            }
            _registry.Delete(loader, environment.GameDirectory);
            PruneOwnedDirectories(environment.GameDirectory, record.Files);
            return LoaderOperationResult.Ok("Loader uninstalled. Mod and user-data folders were preserved.");
        }
        catch (Exception ex) { return LoaderOperationResult.Fail(ex.Message); }
    }

    public IReadOnlyList<DiagnosticItem> Validate(ModLoaderKind loader, GameEnvironmentInfo environment)
    {
        var root = environment.GameDirectory ?? "";
        if (loader == ModLoaderKind.BepInEx)
            return new[]
            {
                new DiagnosticItem("winhttp.dll", File.Exists(Path.Combine(root, "winhttp.dll")), Path.Combine(root, "winhttp.dll")),
                new DiagnosticItem("doorstop_config.ini", File.Exists(Path.Combine(root, "doorstop_config.ini")), Path.Combine(root, "doorstop_config.ini")),
                new DiagnosticItem("BepInEx/core", Directory.Exists(Path.Combine(root, "BepInEx", "core")), Path.Combine(root, "BepInEx", "core")),
                new DiagnosticItem("BepInEx/plugins", Directory.Exists(Path.Combine(root, "BepInEx", "plugins")), Path.Combine(root, "BepInEx", "plugins"))
            };
        if (loader == ModLoaderKind.MelonLoader)
            return new[]
            {
                new DiagnosticItem("version.dll", File.Exists(Path.Combine(root, "version.dll")), Path.Combine(root, "version.dll")),
                new DiagnosticItem("dobby.dll", File.Exists(Path.Combine(root, "dobby.dll")), Path.Combine(root, "dobby.dll")),
                new DiagnosticItem("MelonLoader", Directory.Exists(Path.Combine(root, "MelonLoader")), Path.Combine(root, "MelonLoader")),
                new DiagnosticItem("Mods", Directory.Exists(Path.Combine(root, "Mods")), Path.Combine(root, "Mods")),
                new DiagnosticItem("Plugins", Directory.Exists(Path.Combine(root, "Plugins")), Path.Combine(root, "Plugins"))
            };
        return Array.Empty<DiagnosticItem>();
    }

    private async Task<(string, string)> ResolveMelonDownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(MelonReleaseApi, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "v" + RecommendedMelonLoaderVersion : "v" + RecommendedMelonLoaderVersion;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !name.Contains("x64", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains("android", StringComparison.OrdinalIgnoreCase) || name.Contains("linux", StringComparison.OrdinalIgnoreCase)) continue;
                    var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(url)) return (url, tag.TrimStart('v', 'V'));
                }
            }
        }
        catch
        {
            // Fall back to the known stable Windows x64 asset URL below.
        }
        return ($"https://github.com/LavaGang/MelonLoader/releases/download/v{RecommendedMelonLoaderVersion}/MelonLoader.x64.zip", RecommendedMelonLoaderVersion);
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void SafeExtractZip(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var fullRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(relative)) continue;
            var full = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The loader archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(full);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, true);
        }
    }

    private static string? FindBepInExContentRoot(string extractRoot)
    {
        var candidates = Directory.EnumerateDirectories(extractRoot, "BepInEx", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(x => x is not null)
            .Cast<string>();
        return candidates.FirstOrDefault(root =>
            File.Exists(Path.Combine(root, "winhttp.dll")) &&
            File.Exists(Path.Combine(root, "doorstop_config.ini")));
    }

    private static string? FindMelonContentRoot(string extractRoot)
    {
        var candidates = Directory.EnumerateDirectories(extractRoot, "MelonLoader", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(x => x is not null)
            .Cast<string>();
        return candidates.FirstOrDefault(root =>
            File.Exists(Path.Combine(root, "version.dll")) &&
            File.Exists(Path.Combine(root, "dobby.dll")));
    }

    private static IEnumerable<string> EnumerateLoaderFiles(ModLoaderKind loader, string contentRoot)
    {
        var files = Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var relative = NormalizeRelative(Path.GetRelativePath(contentRoot, file));
            var allowed = loader switch
            {
                ModLoaderKind.BepInEx => relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                                         relative.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase) ||
                                         relative.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase) ||
                                         relative.Equals(".doorstop_version", StringComparison.OrdinalIgnoreCase),
                ModLoaderKind.MelonLoader => relative.StartsWith("MelonLoader/", StringComparison.OrdinalIgnoreCase) ||
                                             relative.Equals("version.dll", StringComparison.OrdinalIgnoreCase) ||
                                             relative.Equals("dobby.dll", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            if (allowed) yield return relative;
        }
    }

    private static void EnsureLoaderDirectories(ModLoaderKind loader, string gameDirectory)
    {
        if (loader == ModLoaderKind.BepInEx)
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, "BepInEx", "plugins"));
            Directory.CreateDirectory(Path.Combine(gameDirectory, "BepInEx", "config"));
            Directory.CreateDirectory(Path.Combine(gameDirectory, "BepInEx", "patchers"));
        }
        else if (loader == ModLoaderKind.MelonLoader)
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, "Mods"));
            Directory.CreateDirectory(Path.Combine(gameDirectory, "Plugins"));
            Directory.CreateDirectory(Path.Combine(gameDirectory, "UserData"));
        }
    }

    private static void BackupExisting(string path, string backupRoot, IDictionary<string, string> backups)
    {
        if (!File.Exists(path) || backups.ContainsKey(path)) return;
        var name = Convert.ToHexString(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(path))) + ".bak";
        var backup = Path.Combine(backupRoot, name);
        File.Copy(path, backup, true);
        backups[path] = backup;
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, NormalizeRelative(relative).Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A loader path escaped the allowed directory.");
        return full;
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static void PruneOwnedDirectories(string gameDirectory, IEnumerable<string> files)
    {
        foreach (var dir in files.Select(x => Path.GetDirectoryName(SafeCombine(gameDirectory, x)))
                     .Where(x => x is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length))
        {
            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
