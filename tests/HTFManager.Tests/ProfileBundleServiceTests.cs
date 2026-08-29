using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Mods;
using HTFManager.Infrastructure.Profiles;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Tests;

public sealed class ProfileBundleServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;
    private readonly ProfileHealthService _health = new();
    private readonly ModRegistryStore _registry;
    private readonly PackageArtifactStore _artifacts;
    private readonly ProfileBundleService _bundles;

    public ProfileBundleServiceTests()
    {
        Directory.CreateDirectory(_root);
        _profiles = new ProfileService(new TestSettingsStore(_root));
        _registry = new ModRegistryStore(_root);
        _artifacts = new PackageArtifactStore(_registry, _root);
        _bundles = new ProfileBundleService(_profiles, _health, _artifacts, _root);
    }

    [Fact]
    public void BuildExportPlan_BundlesOnlyHealthyManagedVerifiedArtifact()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });

        var plan = _bundles.BuildExportPlan(profile, new[] { installed });

        var item = Assert.Single(plan.Items);
        Assert.Equal(ProfileBundleExportDisposition.Bundled, item.Disposition);
        Assert.NotNull(item.Artifact);
        Assert.Equal(1, plan.BundledCount);
    }


    [Fact]
    public void BuildExportPlan_BundlesHealthyManagedLocalArtifactWithIntrinsicIdentity()
    {
        var installed = CreateManagedLocalInstalled("1.2.3", "com.example.local.identity");
        var profile = _profiles.Capture("Local Shared", new[] { installed });

        var item = Assert.Single(_bundles.BuildExportPlan(profile, new[] { installed }).Items);

        Assert.Equal(ProfileBundleExportDisposition.Bundled, item.Disposition);
        Assert.NotNull(item.Artifact);
        Assert.Null(item.Expectation.Requirement.PackageKey);
        Assert.Equal("com.example.local.identity", item.Expectation.Requirement.IntrinsicId);
        Assert.Equal("1.2.3", item.Expectation.Requirement.Version);
    }

    [Fact]
    public void BuildExportPlan_DoesNotBundleLocalArtifactWithoutDeterministicIdentity()
    {
        var installed = CreateManagedLocalInstalled("1.2.3", null);
        var profile = _profiles.Capture("Local Shared", new[] { installed });

        var item = Assert.Single(_bundles.BuildExportPlan(profile, new[] { installed }).Items);

        Assert.Equal(ProfileBundleExportDisposition.Manual, item.Disposition);
        Assert.Null(item.Artifact);
    }

    [Fact]
    public void ExportAndInspectBundle_PreservesLocalIntrinsicIdentity()
    {
        var installed = CreateManagedLocalInstalled("1.2.3", "com.example.local.identity");
        var profile = _profiles.Capture("Local Shared", new[] { installed });
        var path = Path.Combine(_root, "local-shared.htfbundle");

        var result = _bundles.ExportBundle(profile, new[] { installed }, path);
        var inspection = _bundles.InspectBundle(path, Array.Empty<InstalledMod>());

        Assert.True(result.Success, result.Message);
        Assert.True(inspection.IsValid, inspection.Error);
        var payload = Assert.Single(inspection.Manifest!.Payloads);
        Assert.Null(payload.PackageKey);
        Assert.Equal("com.example.local.identity", payload.IntrinsicId);
        Assert.Equal("1.2.3", payload.Version);
        Assert.NotNull(Assert.Single(inspection.Items).BundledPayload);
    }

    [Fact]
    public void BuildExportPlan_DoesNotBundleVersionDrift()
    {
        var installed = CreateManagedInstalled("2.0.0");
        var profile = new ModProfile
        {
            Name = "Shared",
            ExpectedMods = new List<ProfileModExpectation>
            {
                TestData.Expectation(version: "1.2.0", resolvedModId: installed.Id)
            },
            ModStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [installed.Id] = true }
        };

        var item = Assert.Single(_bundles.BuildExportPlan(profile, new[] { installed }).Items);

        Assert.Equal(ProfileBundleExportDisposition.VersionDrift, item.Disposition);
        Assert.Null(item.Artifact);
    }

    [Fact]
    public void ExportAndInspectBundle_HealthyInstalledSuppressesRestoreCandidate()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var path = Path.Combine(_root, "shared.htfbundle");

        var result = _bundles.ExportBundle(profile, new[] { installed }, path);
        var inspection = _bundles.InspectBundle(path, new[] { installed });

        Assert.True(result.Success, result.Message);
        Assert.True(inspection.IsValid, inspection.Error);
        Assert.Equal(1, inspection.HealthyCount);
        Assert.Equal(0, inspection.BundledMissingCount);
        Assert.Null(Assert.Single(inspection.Items).BundledPayload);
        Assert.Single(inspection.Manifest!.Payloads);
    }

    [Fact]
    public void InspectBundle_MissingExactModExposesBundledPayload()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var path = Path.Combine(_root, "shared.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, path).Success);

        var inspection = _bundles.InspectBundle(path, Array.Empty<InstalledMod>());

        Assert.True(inspection.IsValid, inspection.Error);
        Assert.Equal(1, inspection.BundledMissingCount);
        Assert.NotNull(Assert.Single(inspection.Items).BundledPayload);
    }

    [Fact]
    public void InspectBundle_VersionMismatchNeverExposesBundledInstallCandidate()
    {
        var sender = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { sender });
        var path = Path.Combine(_root, "shared.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { sender }, path).Success);
        var receiver = TestData.Installed("receiver", version: "2.0.0");

        var inspection = _bundles.InspectBundle(path, new[] { receiver });

        Assert.True(inspection.IsValid, inspection.Error);
        Assert.Equal(1, inspection.VersionMismatchCount);
        Assert.Null(Assert.Single(inspection.Items).BundledPayload);
    }

    [Fact]
    public void MaterializePayload_VerifiesHashAndPreservesExpectedProvenance()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var path = Path.Combine(_root, "shared.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, path).Success);
        var inspection = _bundles.InspectBundle(path, Array.Empty<InstalledMod>());
        var item = Assert.Single(inspection.Items);

        var materialized = _bundles.MaterializePayload(path, item.BundledPayload!, item.Health.Expectation.Requirement);
        try
        {
            Assert.True(File.Exists(materialized.SourcePath));
            Assert.Equal("Author-ExampleMod", materialized.Metadata.PackageKey);
            Assert.Equal("1.2.0", materialized.Metadata.Version);
            Assert.Equal(ModSourceType.Thunderstore, materialized.Metadata.Source);
        }
        finally
        {
            try { Directory.Delete(materialized.TemporaryDirectory, true); } catch { }
        }
    }

    [Fact]
    public void InspectBundle_RejectsPayloadWithoutDeterministicIdentity()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var exported = Path.Combine(_root, "identity-source.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, exported).Success);
        var mutated = Path.Combine(_root, "identity-missing.htfbundle");
        RewriteBundleManifest(exported, mutated, manifest =>
        {
            var payload = Assert.Single(manifest.Payloads);
            payload.PackageKey = null;
            payload.IntrinsicId = null;
        });

        var inspection = _bundles.InspectBundle(mutated, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsPathTraversalEntry()
    {
        var profilePath = CreateLightweightProfile();
        var bundle = Path.Combine(_root, "bad-path.htfbundle");
        CreateBundle(bundle, profilePath, manifest => { }, extraEntry: "../evil.dll");

        var inspection = _bundles.InspectBundle(bundle, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsDuplicateBundleManifest()
    {
        var profilePath = CreateLightweightProfile();
        var bundle = Path.Combine(_root, "duplicate-manifest.htfbundle");
        var manifest = BasicManifest(profilePath);
        using (var archive = ZipFile.Open(bundle, ZipArchiveMode.Create))
        {
            WriteJson(archive.CreateEntry("bundle.json"), manifest);
            WriteJson(archive.CreateEntry("bundle.json"), manifest);
            AddFile(archive, manifest.ProfileEntry, profilePath);
        }

        var inspection = _bundles.InspectBundle(bundle, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("exactly one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsProfileHashMismatch()
    {
        var profilePath = CreateLightweightProfile();
        var bundle = Path.Combine(_root, "bad-profile-hash.htfbundle");
        CreateBundle(bundle, profilePath, manifest => manifest.ProfileSha256 = new string('A', 64));

        var inspection = _bundles.InspectBundle(bundle, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsPayloadIdentityMismatch()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var exported = Path.Combine(_root, "valid.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, exported).Success);
        var mutated = Path.Combine(_root, "identity-bad.htfbundle");
        RewriteBundleManifest(exported, mutated, manifest => manifest.Payloads[0].Version = "9.9.9");

        var inspection = _bundles.InspectBundle(mutated, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MaterializePayload_RejectsPayloadHashMismatch()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var exported = Path.Combine(_root, "valid.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, exported).Success);
        var mutated = Path.Combine(_root, "hash-bad.htfbundle");
        RewriteBundleManifest(exported, mutated, manifest => manifest.Payloads[0].Sha256 = new string('B', 64));
        var inspection = _bundles.InspectBundle(mutated, Array.Empty<InstalledMod>());
        Assert.True(inspection.IsValid, inspection.Error);
        var item = Assert.Single(inspection.Items);

        var error = Assert.Throws<InvalidDataException>(() =>
            _bundles.MaterializePayload(mutated, item.BundledPayload!, item.Health.Expectation.Requirement));

        Assert.True(error.Message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportEmbeddedProfile_ImportsProfileWithoutInstallingPayload()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Sender", new[] { installed });
        var path = Path.Combine(_root, "shared.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, path).Success);

        var result = _bundles.ImportEmbeddedProfile(path, Array.Empty<InstalledMod>(), "Receiver");

        Assert.True(result.Success, result.Message);
        var imported = Assert.Single(_profiles.LoadProfiles());
        Assert.Equal("Receiver", imported.Name);
        Assert.Single(imported.UnresolvedMods);
        Assert.Single(imported.ExpectedMods);
    }

    [Fact]
    public void BuildExportPlan_UnknownExpectedVersionIsNotBundledAsExact()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = new ModProfile
        {
            Name = "Shared",
            ExpectedMods = new List<ProfileModExpectation>
            {
                TestData.Expectation(version: "—", resolvedModId: installed.Id)
            },
            ModStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [installed.Id] = true }
        };

        var item = Assert.Single(_bundles.BuildExportPlan(profile, new[] { installed }).Items);

        Assert.Equal(ProfileBundleExportDisposition.RemoteOnly, item.Disposition);
        Assert.Null(item.Artifact);
    }

    [Fact]
    public void BuildExportPlan_HealthyThunderstoreWithoutVerifiedCacheIsRemoteOnly()
    {
        var installed = TestData.Installed("managed-no-cache", version: "1.2.0", managed: true);
        var profile = _profiles.Capture("Shared", new[] { installed });

        var item = Assert.Single(_bundles.BuildExportPlan(profile, new[] { installed }).Items);

        Assert.Equal(ProfileBundleExportDisposition.RemoteOnly, item.Disposition);
        Assert.Null(item.Artifact);
    }

    [Fact]
    public void BuildExportPlan_ExternalLocalModIsManualAndNeverBundled()
    {
        var installed = TestData.Installed(
            "external",
            packageKey: null,
            source: ModSourceType.External,
            managed: false);
        var profile = _profiles.Capture("Shared", new[] { installed });

        var item = Assert.Single(_bundles.BuildExportPlan(profile, new[] { installed }).Items);

        Assert.Equal(ProfileBundleExportDisposition.Manual, item.Disposition);
        Assert.Null(item.Artifact);
    }

    [Fact]
    public void InspectBundle_RejectsUnknownSchemaVersion()
    {
        var profilePath = CreateLightweightProfile();
        var bundle = Path.Combine(_root, "unknown-schema.htfbundle");
        CreateBundle(bundle, profilePath, manifest => manifest.SchemaVersion = 99);

        var inspection = _bundles.InspectBundle(bundle, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsNonRootProfileEntry()
    {
        var profilePath = CreateLightweightProfile();
        var bundle = Path.Combine(_root, "nested-profile.htfbundle");
        CreateBundle(bundle, profilePath, manifest => manifest.ProfileEntry = "nested/profile.htfprofile");

        var inspection = _bundles.InspectBundle(bundle, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("root profile.htfprofile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsUnreferencedFileEntry()
    {
        var profilePath = CreateLightweightProfile();
        var bundle = Path.Combine(_root, "unreferenced.htfbundle");
        CreateBundle(bundle, profilePath, manifest => { }, extraEntry: "extra/readme.txt");

        var inspection = _bundles.InspectBundle(bundle, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("unreferenced", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectBundle_RejectsDuplicatePortablePayloadIdentity()
    {
        var installed = CreateManagedInstalled("1.2.0");
        var profile = _profiles.Capture("Shared", new[] { installed });
        var exported = Path.Combine(_root, "valid-duplicate-source.htfbundle");
        Assert.True(_bundles.ExportBundle(profile, new[] { installed }, exported).Success);
        var mutated = Path.Combine(_root, "duplicate-portable-id.htfbundle");
        RewriteBundleManifest(exported, mutated, manifest =>
        {
            var first = manifest.Payloads[0];
            manifest.Payloads.Add(new HtfBundlePayloadDescriptor
            {
                PortableId = first.PortableId,
                PackageKey = first.PackageKey,
                IntrinsicId = first.IntrinsicId,
                Version = first.Version,
                Source = first.Source,
                ArtifactKind = first.ArtifactKind,
                Entry = "payload/duplicate/package.zip",
                Sha256 = first.Sha256,
                UncompressedSize = first.UncompressedSize
            });
        });

        var inspection = _bundles.InspectBundle(mutated, Array.Empty<InstalledMod>());

        Assert.False(inspection.IsValid);
        Assert.True(inspection.Error!.Contains("duplicate portable", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private InstalledMod CreateManagedInstalled(string version)
    {
        var recordId = "record-" + Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes("package-" + version + "-" + recordId);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var packageDir = Path.Combine(_root, "packages", recordId);
        Directory.CreateDirectory(packageDir);
        File.WriteAllBytes(Path.Combine(packageDir, "package.zip"), bytes);
        _registry.Save(new ModInstallationRecord
        {
            Id = recordId,
            GameDirectory = Path.Combine(_root, "game"),
            Name = "Example Mod",
            Version = version,
            Author = "Author",
            PackageKey = "Author-ExampleMod",
            Source = ModSourceType.Thunderstore,
            SourceFileName = "package.zip",
            SourceHash = hash
        });
        return TestData.Installed("mod-" + recordId, version: version, registryId: recordId);
    }

    private InstalledMod CreateManagedLocalInstalled(string version, string? intrinsicId)
    {
        var recordId = "local-record-" + Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes("local-package-" + version + "-" + recordId);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var packageDir = Path.Combine(_root, "packages", recordId);
        Directory.CreateDirectory(packageDir);
        File.WriteAllBytes(Path.Combine(packageDir, "local-package.zip"), bytes);
        _registry.Save(new ModInstallationRecord
        {
            Id = recordId,
            GameDirectory = Path.Combine(_root, "game"),
            Name = "Local Identity Test Mod",
            Version = version,
            Author = "Local",
            IntrinsicId = intrinsicId,
            Source = ModSourceType.LocalArchive,
            SourceFileName = "local-package.zip",
            SourceHash = hash
        });
        return TestData.Installed(
            "mod-" + recordId,
            name: "Local Identity Test Mod",
            version: version,
            packageKey: null,
            intrinsicId: intrinsicId,
            source: ModSourceType.LocalArchive,
            registryId: recordId);
    }

    private string CreateLightweightProfile()
    {
        var installed = TestData.Installed("profile-mod", version: "1.0.0");
        var profile = _profiles.Capture("Profile", new[] { installed });
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".htfprofile");
        var result = _profiles.ExportPortablePackage(profile, new[] { installed }, path);
        Assert.True(result.Success, result.Message);
        return path;
    }

    private static HtfBundleManifest BasicManifest(string profilePath)
        => new()
        {
            SchemaVersion = 1,
            GeneratedWithVersion = "0.3.7",
            ProfileEntry = "profile.htfprofile",
            ProfileSha256 = HashFile(profilePath)
        };

    private static void CreateBundle(
        string bundlePath,
        string profilePath,
        Action<HtfBundleManifest> mutate,
        string? extraEntry = null)
    {
        var manifest = BasicManifest(profilePath);
        mutate(manifest);
        using var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create);
        WriteJson(archive.CreateEntry("bundle.json"), manifest);
        AddFile(archive, manifest.ProfileEntry, profilePath);
        if (extraEntry is not null)
        {
            var entry = archive.CreateEntry(extraEntry);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("evil");
        }
    }

    private static void RewriteBundleManifest(string source, string destination, Action<HtfBundleManifest> mutate)
    {
        using var input = ZipFile.OpenRead(source);
        using var manifestStream = input.Entries.Single(entry => entry.FullName == "bundle.json").Open();
        var manifest = JsonSerializer.Deserialize<HtfBundleManifest>(manifestStream, JsonOptions())!;
        mutate(manifest);

        using var output = ZipFile.Open(destination, ZipArchiveMode.Create);
        WriteJson(output.CreateEntry("bundle.json"), manifest);
        foreach (var entry in input.Entries.Where(entry => entry.FullName != "bundle.json" && !string.IsNullOrEmpty(entry.Name)))
        {
            var copy = output.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
            using var from = entry.Open();
            using var to = copy.Open();
            from.CopyTo(to);
        }
    }

    private static void WriteJson(ZipArchiveEntry entry, HtfBundleManifest manifest)
    {
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, JsonOptions());
    }

    private static void AddFile(ZipArchive archive, string name, string path)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var from = File.OpenRead(path);
        using var to = entry.Open();
        from.CopyTo(to);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class TestSettingsStore(string dataDirectory) : ISettingsStore
    {
        public string DataDirectory { get; } = dataDirectory;
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
