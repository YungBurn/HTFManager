using System.IO.Compression;
using System.Text.Json;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Profiles;

namespace HTFManager.Tests;

public sealed class ProfileServiceExpectationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly TestSettingsStore _settings;
    private readonly ProfileService _profiles;

    public ProfileServiceExpectationTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new TestSettingsStore(_root);
        _profiles = new ProfileService(_settings);
    }

    [Fact]
    public void Capture_PersistsCompleteExpectedState()
    {
        var installed = TestData.Installed("mod-a", version: "2.4.0", enabled: false);

        var profile = _profiles.Capture("Captured", new[] { installed });
        var expectation = Assert.Single(profile.ExpectedMods);

        Assert.Equal(ProfileExpectationMetadataQuality.Complete, expectation.MetadataQuality);
        Assert.Equal("mod-a", expectation.ResolvedModId);
        Assert.Equal("Author-ExampleMod", expectation.Requirement.PackageKey);
        Assert.Equal("2.4.0", expectation.Requirement.Version);
        Assert.False(expectation.Requirement.Enabled);
    }


    [Fact]
    public void AddMod_CreatesCompleteExpectation()
    {
        var profile = new ModProfile { Name = "Manual" };
        var installed = TestData.Installed("mod-a", version: "3.0.0", enabled: true);

        _profiles.AddMod(profile, installed);

        var expectation = Assert.Single(profile.ExpectedMods);
        Assert.Equal(ProfileExpectationMetadataQuality.Complete, expectation.MetadataQuality);
        Assert.Equal("mod-a", expectation.ResolvedModId);
        Assert.Equal("3.0.0", expectation.Requirement.Version);
        Assert.True(profile.ModStates["mod-a"]);
    }

    [Fact]
    public void SetModState_UpdatesExpectedEnabledState()
    {
        var installed = TestData.Installed("mod-a", enabled: true);
        var profile = _profiles.Capture("Captured", new[] { installed });

        _profiles.SetModState(profile, "mod-a", false);

        Assert.False(profile.ModStates["mod-a"]);
        Assert.False(Assert.Single(profile.ExpectedMods).Requirement.Enabled);
    }

    [Fact]
    public void RemoveMod_RemovesResolvedExpectation()
    {
        var installed = TestData.Installed("mod-a");
        var profile = _profiles.Capture("Captured", new[] { installed });

        _profiles.RemoveMod(profile, "mod-a");

        Assert.Empty(profile.ModStates);
        Assert.Empty(profile.ExpectedMods);
    }

    [Fact]
    public void LoadProfiles_MigratesV036ResolvedBindingsWithoutInventingVersion()
    {
        var profilesDirectory = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        File.WriteAllText(
            Path.Combine(profilesDirectory, "legacy.json"),
            """
            {
              "Name": "Legacy",
              "ModStates": { "legacy-local-id": true },
              "ConfigurationSnapshots": [],
              "UnresolvedMods": []
            }
            """);

        var profile = Assert.Single(_profiles.LoadProfiles());
        var expectation = Assert.Single(profile.ExpectedMods);

        Assert.Equal(ProfileExpectationMetadataQuality.LegacyBindingOnly, expectation.MetadataQuality);
        Assert.Equal("legacy-local-id", expectation.ResolvedModId);
        Assert.Equal("—", expectation.Requirement.Version);
        Assert.Equal("legacy-local-id", expectation.Requirement.Name);
        Assert.True(expectation.Requirement.Enabled);
    }

    [Fact]
    public void LoadProfiles_MigratesV036UnresolvedRequirementAsCompleteExpectation()
    {
        var profilesDirectory = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        File.WriteAllText(
            Path.Combine(profilesDirectory, "legacy.json"),
            """
            {
              "Name": "Legacy",
              "ModStates": {},
              "ConfigurationSnapshots": [],
              "UnresolvedMods": [
                {
                  "PortableId": "portable-a",
                  "Name": "Example Mod",
                  "Version": "1.2.0",
                  "Author": "Author",
                  "PackageKey": "Author-ExampleMod",
                  "FileName": "ExampleMod.dll",
                  "Source": 3,
                  "Loader": 1,
                  "Component": 1,
                  "Enabled": true
                }
              ]
            }
            """);

        var profile = Assert.Single(_profiles.LoadProfiles());
        var expectation = Assert.Single(profile.ExpectedMods);

        Assert.Equal(ProfileExpectationMetadataQuality.Complete, expectation.MetadataQuality);
        Assert.Null(expectation.ResolvedModId);
        Assert.Equal("Author-ExampleMod", expectation.Requirement.PackageKey);
        Assert.Equal("1.2.0", expectation.Requirement.Version);
    }

    [Fact]
    public void Export_LegacyBindingDoesNotInventHistoricalVersion()
    {
        var profilesDirectory = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        File.WriteAllText(
            Path.Combine(profilesDirectory, "legacy.json"),
            """
            {
              "Name": "Legacy",
              "ModStates": { "legacy-local-id": true },
              "ConfigurationSnapshots": [],
              "UnresolvedMods": []
            }
            """);
        var profile = Assert.Single(_profiles.LoadProfiles());
        var installed = TestData.Installed("legacy-local-id", version: "9.9.9");
        var exportPath = Path.Combine(_root, "legacy-export.htfprofile");

        var result = _profiles.ExportPortablePackage(profile, new[] { installed }, exportPath);

        Assert.True(result.Success, result.Message);
        using var archive = ZipFile.OpenRead(exportPath);
        using var document = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
        var mod = Assert.Single(document.RootElement.GetProperty("Mods").EnumerateArray().ToArray());
        Assert.Equal("—", mod.GetProperty("Version").GetString());
        Assert.Equal("Author-ExampleMod", mod.GetProperty("PackageKey").GetString());
    }

    [Fact]
    public void Import_SamePackageDifferentInstalledVersion_PreservesExpectedVersion()
    {
        var package = CreatePortableProfile("Shared", "portable-a", "1.2.0", "Author-ExampleMod");
        var installed = TestData.Installed("installed-a", version: "2.0.0");

        var result = _profiles.ImportPortablePackage(package, new[] { installed }, "Imported");

        Assert.True(result.Success, result.Message);
        var profile = Assert.Single(_profiles.LoadProfiles());
        var expectation = Assert.Single(profile.ExpectedMods);
        Assert.Equal("1.2.0", expectation.Requirement.Version);
        Assert.Equal("installed-a", expectation.ResolvedModId);
        Assert.Empty(profile.UnresolvedMods);

        var health = new ProfileHealthService().Evaluate(profile, new[] { installed });
        Assert.Equal(ProfileHealthStatus.VersionMismatch, Assert.Single(health.Items).Status);
    }

    [Fact]
    public void Export_UsesExpectedVersionInsteadOfCurrentInstalledDrift()
    {
        var package = CreatePortableProfile("Shared", "portable-a", "1.2.0", "Author-ExampleMod");
        var installed = TestData.Installed("installed-a", version: "2.0.0");
        Assert.True(_profiles.ImportPortablePackage(package, new[] { installed }, "Imported").Success);
        var profile = Assert.Single(_profiles.LoadProfiles());
        var exportPath = Path.Combine(_root, "exported.htfprofile");

        var result = _profiles.ExportPortablePackage(profile, new[] { installed }, exportPath);

        Assert.True(result.Success, result.Message);
        using var archive = ZipFile.OpenRead(exportPath);
        using var document = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
        var mod = Assert.Single(document.RootElement.GetProperty("Mods").EnumerateArray().ToArray());
        Assert.Equal("1.2.0", mod.GetProperty("Version").GetString());
        Assert.Equal("Author-ExampleMod", mod.GetProperty("PackageKey").GetString());
    }

    [Fact]
    public void ResolveMissing_SetsBindingButPreservesOriginalExpectedVersion()
    {
        var package = CreatePortableProfile("Shared", "portable-a", "1.2.0", "Author-ExampleMod");
        Assert.True(_profiles.ImportPortablePackage(package, Array.Empty<InstalledMod>(), "Imported").Success);
        var profile = Assert.Single(_profiles.LoadProfiles());
        var installed = TestData.Installed("installed-a", version: "2.0.0");

        var result = _profiles.ResolveMissingMods(profile, new[] { installed });

        Assert.True(result.Success, result.Message);
        Assert.Empty(profile.UnresolvedMods);
        var expectation = Assert.Single(profile.ExpectedMods);
        Assert.Equal("installed-a", expectation.ResolvedModId);
        Assert.Equal("1.2.0", expectation.Requirement.Version);
        Assert.Equal(ProfileHealthStatus.VersionMismatch,
            Assert.Single(new ProfileHealthService().Evaluate(profile, new[] { installed }).Items).Status);
    }

    [Fact]
    public void RemoveMissingMod_RemovesCanonicalExpectationToo()
    {
        var package = CreatePortableProfile("Shared", "portable-a", "1.2.0", "Author-ExampleMod");
        Assert.True(_profiles.ImportPortablePackage(package, Array.Empty<InstalledMod>(), "Imported").Success);
        var profile = Assert.Single(_profiles.LoadProfiles());

        _profiles.RemoveMissingMod(profile, "portable-a");

        Assert.Empty(profile.UnresolvedMods);
        Assert.Empty(profile.ExpectedMods);
    }

    [Fact]
    public void ResolveMissing_RebuildsProjectionForModRemovedAfterProfileCreation()
    {
        var installed = TestData.Installed("mod-a", version: "1.2.0");
        var profile = _profiles.Capture("Captured", new[] { installed });
        Assert.Empty(profile.UnresolvedMods);

        var result = _profiles.ResolveMissingMods(profile, Array.Empty<InstalledMod>());

        Assert.True(result.Success, result.Message);
        var unresolved = Assert.Single(profile.UnresolvedMods);
        Assert.Equal("Author-ExampleMod", unresolved.PackageKey);
        Assert.Equal("1.2.0", unresolved.Version);
        Assert.Null(Assert.Single(profile.ExpectedMods).ResolvedModId);
        Assert.Empty(profile.ModStates);
    }

    [Fact]
    public void Apply_BlocksExpectedModThatWasRemovedAfterProfileCreation()
    {
        var installed = TestData.Installed("mod-a");
        var profile = _profiles.Capture("Captured", new[] { installed });

        var result = _profiles.Apply(profile, Array.Empty<InstalledMod>(), new NoOpModService(), _root);

        Assert.False(result.Success);
        Assert.True(result.Message.Contains("missing expected mod", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private string CreatePortableProfile(string profileName, string portableId, string version, string packageKey)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".htfprofile");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("manifest.json");
        using var stream = entry.Open();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("Format", "HTFManager.Profile");
        writer.WriteNumber("SchemaVersion", 1);
        writer.WriteString("ExportedWithVersion", "0.3.6");
        writer.WriteString("ExportedUtc", DateTimeOffset.UtcNow);
        writer.WriteString("ProfileName", profileName);
        writer.WriteStartArray("Mods");
        writer.WriteStartObject();
        writer.WriteString("PortableId", portableId);
        writer.WriteString("Name", "Example Mod");
        writer.WriteString("Version", version);
        writer.WriteString("Author", "Author");
        writer.WriteString("PackageKey", packageKey);
        writer.WriteString("FileName", "ExampleMod.dll");
        writer.WriteString("Source", "Thunderstore");
        writer.WriteString("Loader", "BepInEx");
        writer.WriteString("Component", "Plugin");
        writer.WriteBoolean("Enabled", true);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteStartArray("Configurations");
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return path;
    }

    private sealed class TestSettingsStore(string dataDirectory) : ISettingsStore
    {
        public string DataDirectory { get; } = dataDirectory;
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }

    private sealed class NoOpModService : IModService
    {
        public IReadOnlyList<InstalledMod> Scan(GameEnvironmentInfo environment) => Array.Empty<InstalledMod>();
        public bool SetEnabled(InstalledMod mod, bool enabled) => true;
    }
}
