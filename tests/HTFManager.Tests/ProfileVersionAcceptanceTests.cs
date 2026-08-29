using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Profiles;

namespace HTFManager.Tests;

public sealed class ProfileVersionAcceptanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));

    public ProfileVersionAcceptanceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AcceptInstalledVersion_ChangesOnlyVersionForMatchingPackageIdentity()
    {
        var service = new ProfileService(new TestSettingsStore(_root));
        var profile = new ModProfile
        {
            Name = "Profile",
            ExpectedMods = new List<ProfileModExpectation>
            {
                TestData.Expectation(portableId: "p", version: "1.0.0", resolvedModId: "old")
            },
            ModStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["old"] = false,
                ["installed"] = true
            }
        };
        var installed = TestData.Installed("installed", version: "2.0.0", packageKey: "Author-ExampleMod");

        var result = service.AcceptInstalledVersion(profile, "p", installed);

        Assert.True(result.Success, result.Message);
        var expectation = Assert.Single(profile.ExpectedMods);
        Assert.Equal("2.0.0", expectation.Requirement.Version);
        Assert.Equal("Author-ExampleMod", expectation.Requirement.PackageKey);
        Assert.Equal("installed", expectation.ResolvedModId);
        Assert.False(profile.ModStates.ContainsKey("old"));
        Assert.True(profile.ModStates["installed"]);
    }

    [Fact]
    public void AcceptInstalledVersion_RejectsDifferentLogicalSource()
    {
        var service = new ProfileService(new TestSettingsStore(_root));
        var profile = new ModProfile
        {
            Name = "Profile",
            ExpectedMods = new List<ProfileModExpectation>
            {
                TestData.Expectation(portableId: "p", version: "1.0.0", packageKey: "Author-Expected")
            }
        };
        var installed = TestData.Installed(
            "installed",
            version: "2.0.0",
            packageKey: "Author-Expected",
            source: ModSourceType.LocalArchive);

        var result = service.AcceptInstalledVersion(profile, "p", installed);

        Assert.False(result.Success);
        Assert.Equal("1.0.0", profile.ExpectedMods.Single().Requirement.Version);
    }

    [Fact]
    public void AcceptInstalledVersion_RejectsDifferentPackageIdentity()
    {
        var service = new ProfileService(new TestSettingsStore(_root));
        var profile = new ModProfile
        {
            Name = "Profile",
            ExpectedMods = new List<ProfileModExpectation>
            {
                TestData.Expectation(portableId: "p", version: "1.0.0", packageKey: "Author-Expected")
            }
        };
        var installed = TestData.Installed("installed", version: "2.0.0", packageKey: "Author-Other");

        var result = service.AcceptInstalledVersion(profile, "p", installed);

        Assert.False(result.Success);
        Assert.Equal("1.0.0", profile.ExpectedMods.Single().Requirement.Version);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private sealed class TestSettingsStore(string dataDirectory) : ISettingsStore
    {
        public string DataDirectory { get; } = dataDirectory;
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
