using System.Security.Cryptography;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Mods;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Tests;

public sealed class PackageArtifactHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));

    public PackageArtifactHistoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CaptureArtifact_PreservesMultipleExactVersions()
    {
        var registry = new ModRegistryStore(_root);
        var store = new PackageArtifactStore(registry, _root);
        var v1 = CreateArtifact("v1.zip", "version-one");
        var v2 = CreateArtifact("v2.zip", "version-two");
        var record = Record("1.0.0", v1);
        store.CaptureArtifact(record, v1);
        record.Version = "2.0.0";
        record.SourceHash = Hash(v2);
        record.SourceFileName = Path.GetFileName(v2);
        store.CaptureArtifact(record, v2);

        var first = store.FindExactArtifact(TestData.Expectation(version: "1.0.0").Requirement);
        var second = store.FindExactArtifact(TestData.Expectation(version: "2.0.0").Requirement);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("1.0.0", first!.Version);
        Assert.Equal("2.0.0", second!.Version);
        Assert.NotEqual(first.Sha256, second.Sha256);
    }

    [Fact]
    public void FindExactArtifact_RequiresExactSourceAndIdentity()
    {
        var store = new PackageArtifactStore(new ModRegistryStore(_root), _root);
        var path = CreateArtifact("local.zip", "local");
        var record = Record("1.0.0", path);
        record.PackageKey = null;
        record.IntrinsicId = "com.example.local";
        record.Source = ModSourceType.LocalArchive;
        store.CaptureArtifact(record, path);

        var localRequirement = TestData.Expectation(
            version: "1.0.0",
            packageKey: null,
            intrinsicId: "com.example.local").Requirement;
        localRequirement.Source = ModSourceType.LocalArchive;
        Assert.NotNull(store.FindExactArtifact(localRequirement));
        Assert.Null(store.FindExactArtifact(TestData.Expectation(
            version: "1.0.0",
            packageKey: "Author-ExampleMod",
            intrinsicId: null).Requirement));
    }

    private ModInstallationRecord Record(string version, string source)
        => new()
        {
            Id = "record",
            GameDirectory = Path.Combine(_root, "game"),
            Name = "Example Mod",
            Version = version,
            PackageKey = "Author-ExampleMod",
            Source = ModSourceType.Thunderstore,
            SourceFileName = Path.GetFileName(source),
            SourceHash = Hash(source)
        };

    private string CreateArtifact(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
