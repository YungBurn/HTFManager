using System.Security.Cryptography;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Mods;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Tests;

public sealed class PackageArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));

    public PackageArtifactStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void FindVerifiedArtifact_ReturnsOnlyHashMatchingManagedCache()
    {
        var registry = new ModRegistryStore(_root);
        var bytes = "verified-package"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        registry.Save(new ModInstallationRecord
        {
            Id = "record-a",
            GameDirectory = Path.Combine(_root, "game"),
            Name = "Example Mod",
            Version = "1.0.0",
            PackageKey = "Author-ExampleMod",
            Source = ModSourceType.Thunderstore,
            SourceFileName = "package.zip",
            SourceHash = hash
        });
        var packageDir = Path.Combine(_root, "packages", "record-a");
        Directory.CreateDirectory(packageDir);
        File.WriteAllBytes(Path.Combine(packageDir, "stale.zip"), "stale"u8.ToArray());
        File.WriteAllBytes(Path.Combine(packageDir, "package.zip"), bytes);

        var installed = TestData.Installed("mod-a", registryId: "record-a");
        var artifact = new PackageArtifactStore(registry, _root).FindVerifiedArtifact(installed);

        Assert.NotNull(artifact);
        Assert.Equal(hash, artifact!.Sha256);
        Assert.Equal("package.zip", artifact.FileName);
        Assert.Equal(HtfBundleArtifactKind.Archive, artifact.Kind);
    }

    [Fact]
    public void FindVerifiedArtifact_ReturnsNullForExternalOrCorruptCache()
    {
        var registry = new ModRegistryStore(_root);
        registry.Save(new ModInstallationRecord
        {
            Id = "record-a",
            GameDirectory = Path.Combine(_root, "game"),
            Name = "Example Mod",
            Version = "1.0.0",
            SourceHash = new string('A', 64)
        });
        var packageDir = Path.Combine(_root, "packages", "record-a");
        Directory.CreateDirectory(packageDir);
        File.WriteAllBytes(Path.Combine(packageDir, "package.zip"), "wrong"u8.ToArray());

        var store = new PackageArtifactStore(registry, _root);
        Assert.Null(store.FindVerifiedArtifact(TestData.Installed("managed", registryId: "record-a")));
        Assert.Null(store.FindVerifiedArtifact(TestData.Installed("external", managed: false, registryId: "record-a")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
