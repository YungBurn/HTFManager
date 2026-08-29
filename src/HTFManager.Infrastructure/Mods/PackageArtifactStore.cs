using System.Security.Cryptography;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Infrastructure.Mods;

public sealed class PackageArtifactStore : IPackageArtifactStore
{
    private readonly ModRegistryStore _registry;
    private readonly string _dataDirectory;

    public PackageArtifactStore(ModRegistryStore registry, string dataDirectory)
    {
        _registry = registry;
        _dataDirectory = dataDirectory;
    }

    public PackageArtifact? FindVerifiedArtifact(InstalledMod mod)
    {
        if (!mod.IsManaged || string.IsNullOrWhiteSpace(mod.RegistryId)) return null;

        var record = _registry.Find(mod.RegistryId!);
        if (record is null || string.IsNullOrWhiteSpace(record.SourceHash)) return null;

        var directory = Path.Combine(_dataDirectory, "packages", record.Id);
        if (!Directory.Exists(directory)) return null;

        IEnumerable<string> candidates;
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
                        : HtfBundleArtifactKind.Archive
                };
            }
            catch
            {
                // A stale/corrupt cache candidate is not a valid source artifact.
            }
        }

        return null;
    }

    private static bool IsSupportedArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
