using System.Security.Cryptography;
using System.Text.Json;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Infrastructure.Mods;

public sealed class PackageArtifactStore : IPackageArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly ModRegistryStore _registry;
    private readonly string _dataDirectory;
    private readonly string _historyRoot;
    private readonly string _historyFilesRoot;
    private readonly string _historyIndexPath;

    public PackageArtifactStore(ModRegistryStore registry, string dataDirectory)
    {
        _registry = registry;
        _dataDirectory = dataDirectory;
        _historyRoot = Path.Combine(dataDirectory, "packages", "history");
        _historyFilesRoot = Path.Combine(_historyRoot, "files");
        _historyIndexPath = Path.Combine(_historyRoot, "artifacts.json");
    }

    public PackageArtifact? FindVerifiedArtifact(InstalledMod mod)
    {
        if (!mod.IsManaged || string.IsNullOrWhiteSpace(mod.RegistryId)) return null;

        var record = _registry.Find(mod.RegistryId!);
        if (record is null || string.IsNullOrWhiteSpace(record.SourceHash)) return null;

        var fromHistory = FindHistoryArtifact(
            record.PackageKey,
            record.IntrinsicId,
            record.Version,
            record.Source,
            record.SourceHash,
            record.Id);
        if (fromHistory is not null) return fromHistory;

        var legacy = FindLegacyVerifiedArtifact(record);
        if (legacy is not null)
        {
            try { CaptureArtifact(record, legacy.Path); } catch { }
        }
        return legacy;
    }

    public PackageArtifact? FindExactArtifact(ProfileModRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (VersionUnknown(requirement.Version) || !HasDeterministicIdentity(requirement)) return null;

        var history = LoadHistory()
            .Where(record => IdentityMatches(record, requirement))
            .Where(record => record.Source == requirement.Source)
            .Where(record => SameVersion(record.Version, requirement.Version))
            .OrderByDescending(record => record.CapturedUtc)
            .ToArray();

        foreach (var record in history)
        {
            var artifact = MaterializeVerified(record);
            if (artifact is not null) return artifact;
        }

        var currentRecord = _registry.LoadAll()
            .Where(record => IdentityMatches(record, requirement))
            .Where(record => record.Source == requirement.Source)
            .Where(record => SameVersion(record.Version, requirement.Version))
            .OrderByDescending(record => record.InstalledAt)
            .FirstOrDefault();
        if (currentRecord is null) return null;

        var legacy = FindLegacyVerifiedArtifact(currentRecord);
        if (legacy is null) return null;
        try { CaptureArtifact(currentRecord, legacy.Path); } catch { }
        return legacy;
    }

    public void PreserveCurrentArtifact(ModInstallationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var legacy = FindLegacyVerifiedArtifact(record);
        if (legacy is null) return;
        try { CaptureArtifact(record, legacy.Path); } catch { }
    }

    public void CaptureArtifact(ModInstallationRecord record, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
        if (VersionUnknown(record.Version)) return;
        if (string.IsNullOrWhiteSpace(record.PackageKey) && string.IsNullOrWhiteSpace(record.IntrinsicId)) return;

        var hash = ComputeSha256(sourcePath);
        if (!string.IsNullOrWhiteSpace(record.SourceHash) &&
            !hash.Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase))
            return;

        var extension = Path.GetExtension(sourcePath).Equals(".dll", StringComparison.OrdinalIgnoreCase) ? ".dll" : ".zip";
        var kind = extension == ".dll" ? HtfBundleArtifactKind.Assembly : HtfBundleArtifactKind.Archive;
        var info = new FileInfo(sourcePath);
        var storedRelative = Path.Combine("files", hash.ToLowerInvariant() + extension).Replace('\\', '/');
        var storedPath = Path.Combine(_historyRoot, storedRelative.Replace('/', Path.DirectorySeparatorChar));

        lock (_gate)
        {
            Directory.CreateDirectory(_historyFilesRoot);
            if (!File.Exists(storedPath))
                File.Copy(sourcePath, storedPath, false);

            var records = ReadHistoryUnlocked();
            var existing = records.FirstOrDefault(item =>
                item.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase) &&
                IdentityMatches(item, record.PackageKey, record.IntrinsicId) &&
                item.Source == record.Source &&
                SameVersion(item.Version, record.Version));

            if (existing is null)
            {
                records.Add(new PackageArtifactRecord
                {
                    PackageKey = NormalizeOptional(record.PackageKey),
                    IntrinsicId = NormalizeOptional(record.IntrinsicId),
                    Version = record.Version.Trim(),
                    Source = record.Source,
                    Kind = kind,
                    FileName = Path.GetFileName(sourcePath),
                    Sha256 = hash,
                    Length = info.Length,
                    StoredPath = storedRelative,
                    CapturedUtc = DateTimeOffset.UtcNow
                });
                WriteHistoryUnlocked(records);
            }
        }
    }

    private PackageArtifact? FindHistoryArtifact(
        string? packageKey,
        string? intrinsicId,
        string version,
        ModSourceType source,
        string sourceHash,
        string registryId)
    {
        foreach (var record in LoadHistory()
                     .Where(item => IdentityMatches(item, packageKey, intrinsicId))
                     .Where(item => item.Source == source)
                     .Where(item => SameVersion(item.Version, version))
                     .Where(item => item.Sha256.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(item => item.CapturedUtc))
        {
            var artifact = MaterializeVerified(record, registryId);
            if (artifact is not null) return artifact;
        }

        return null;
    }

    private PackageArtifact? FindLegacyVerifiedArtifact(ModInstallationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SourceHash)) return null;
        var directory = Path.Combine(_dataDirectory, "packages", record.Id);
        if (!Directory.Exists(directory)) return null;

        string[] candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedArtifact)
                .OrderByDescending(path => !string.IsNullOrWhiteSpace(record.SourceFileName) &&
                                           Path.GetFileName(path).Equals(record.SourceFileName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return null;
        }

        foreach (var path in candidates)
        {
            try
            {
                var hash = ComputeSha256(path);
                if (!hash.Equals(record.SourceHash, StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(path);
                return new PackageArtifact
                {
                    RegistryId = record.Id,
                    Path = path,
                    FileName = info.Name,
                    Sha256 = hash,
                    Length = info.Length,
                    Kind = Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                        ? HtfBundleArtifactKind.Assembly
                        : HtfBundleArtifactKind.Archive,
                    PackageKey = record.PackageKey,
                    IntrinsicId = record.IntrinsicId,
                    Version = record.Version,
                    Source = record.Source
                };
            }
            catch
            {
            }
        }

        return null;
    }

    private PackageArtifact? MaterializeVerified(PackageArtifactRecord record, string registryId = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(record.StoredPath)) return null;
            var fullRoot = Path.GetFullPath(_historyRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(_historyRoot, record.StoredPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) return null;

            var info = new FileInfo(fullPath);
            if (info.Length != record.Length) return null;
            var hash = ComputeSha256(fullPath);
            if (!hash.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase)) return null;

            return new PackageArtifact
            {
                RegistryId = registryId,
                Path = fullPath,
                FileName = record.FileName,
                Sha256 = hash,
                Length = info.Length,
                Kind = record.Kind,
                PackageKey = record.PackageKey,
                IntrinsicId = record.IntrinsicId,
                Version = record.Version,
                Source = record.Source
            };
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<PackageArtifactRecord> LoadHistory()
    {
        lock (_gate)
            return ReadHistoryUnlocked().ToArray();
    }

    private List<PackageArtifactRecord> ReadHistoryUnlocked()
    {
        try
        {
            if (!File.Exists(_historyIndexPath)) return new List<PackageArtifactRecord>();
            return JsonSerializer.Deserialize<List<PackageArtifactRecord>>(File.ReadAllText(_historyIndexPath), JsonOptions)
                   ?? new List<PackageArtifactRecord>();
        }
        catch
        {
            return new List<PackageArtifactRecord>();
        }
    }

    private void WriteHistoryUnlocked(List<PackageArtifactRecord> records)
    {
        Directory.CreateDirectory(_historyRoot);
        var temp = _historyIndexPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(temp, _historyIndexPath, true);
    }

    private static bool IdentityMatches(PackageArtifactRecord record, ProfileModRequirement requirement)
        => IdentityMatches(record, requirement.PackageKey, requirement.IntrinsicId);

    private static bool IdentityMatches(PackageArtifactRecord record, string? packageKey, string? intrinsicId)
    {
        if (!string.IsNullOrWhiteSpace(packageKey))
            return !string.IsNullOrWhiteSpace(record.PackageKey) &&
                   record.PackageKey!.Equals(packageKey.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(intrinsicId))
            return string.IsNullOrWhiteSpace(record.PackageKey) &&
                   !string.IsNullOrWhiteSpace(record.IntrinsicId) &&
                   record.IntrinsicId!.Equals(intrinsicId.Trim(), StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool IdentityMatches(ModInstallationRecord record, ProfileModRequirement requirement)
    {
        if (!string.IsNullOrWhiteSpace(requirement.PackageKey))
            return !string.IsNullOrWhiteSpace(record.PackageKey) &&
                   record.PackageKey!.Equals(requirement.PackageKey.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(requirement.IntrinsicId))
            return string.IsNullOrWhiteSpace(record.PackageKey) &&
                   !string.IsNullOrWhiteSpace(record.IntrinsicId) &&
                   record.IntrinsicId!.Equals(requirement.IntrinsicId.Trim(), StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool IsSupportedArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDeterministicIdentity(ProfileModRequirement requirement)
        => !string.IsNullOrWhiteSpace(requirement.PackageKey) || !string.IsNullOrWhiteSpace(requirement.IntrinsicId);

    private static bool VersionUnknown(string? version)
        => string.IsNullOrWhiteSpace(version) || version.Trim() == "—";

    private static bool SameVersion(string? left, string? right)
        => !VersionUnknown(left) && !VersionUnknown(right) &&
           left!.Trim().Equals(right!.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
