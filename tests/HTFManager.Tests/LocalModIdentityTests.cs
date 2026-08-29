using System.IO.Compression;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Mods;
using HTFManager.Infrastructure.Profiles;
using HTFManager.Infrastructure.Storage;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version)
        {
            GUID = guid;
            Name = name;
            Version = version;
        }

        public string GUID { get; }
        public string Name { get; }
        public string Version { get; }
    }

    public abstract class BaseUnityPlugin { }
}

namespace HTFManager.Tests.Fixtures
{
    [BepInEx.BepInPlugin("com.example.local.identity", "Local Identity Test Mod", "1.2.3")]
    public sealed class LocalIdentityPlugin : BepInEx.BaseUnityPlugin { }
}

namespace HTFManager.Tests
{
    public sealed class LocalModIdentityTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "HTFManager.Tests", Guid.NewGuid().ToString("N"));

        public LocalModIdentityTests() => Directory.CreateDirectory(_root);

        [Fact]
        public async Task InspectLocalZip_UsesUniqueBepInPluginIdentityWhenManifestIsMissing()
        {
            var zip = CreateSingleAssemblyZip("TrueDotCrosshair.zip");
            var service = new ModPackageService(new ModRegistryStore(_root), _root);

            var inspection = await service.InspectAsync(zip, TestContext.Current.CancellationToken);

            Assert.True(inspection.IsValid, inspection.Error);
            Assert.Null(inspection.PackageKey);
            Assert.Equal("com.example.local.identity", inspection.IntrinsicId);
            Assert.Equal("Local Identity Test Mod", inspection.Name);
            Assert.Equal("1.2.3", inspection.Version);
            Assert.Equal(ModSourceType.LocalArchive, inspection.Source);
            Assert.Equal(ModLoaderKind.BepInEx, inspection.Loader);
        }

        [Fact]
        public void ProfileHealth_PrefersIntrinsicIdentityForLocalMods()
        {
            var expectation = TestData.Expectation(
                name: "Old File Name",
                version: "1.2.3",
                packageKey: null,
                intrinsicId: "com.example.local.identity",
                fileName: "Old.dll",
                resolvedModId: "stale");
            var installed = TestData.Installed(
                "current",
                name: "Local Identity Test Mod",
                version: "1.2.3",
                packageKey: null,
                intrinsicId: "com.example.local.identity",
                fileName: "Renamed.dll",
                source: ModSourceType.LocalArchive);
            var profile = new ModProfile
            {
                Name = "Local Profile",
                ExpectedMods = new List<ProfileModExpectation> { expectation }
            };

            var item = Assert.Single(new ProfileHealthService().Evaluate(profile, new[] { installed }).Items);

            Assert.Equal(ProfileHealthStatus.Healthy, item.Status);
            Assert.Equal(ProfileHealthMatchKind.IntrinsicId, item.MatchKind);
            Assert.Same(installed, item.InstalledMod);
        }

        [Fact]
        public void ProfileHealth_DuplicateIntrinsicIdentityIsUncertain()
        {
            var expectation = TestData.Expectation(
                packageKey: null,
                intrinsicId: "com.example.local.identity",
                resolvedModId: null);
            var first = TestData.Installed("a", packageKey: null, intrinsicId: "com.example.local.identity", source: ModSourceType.LocalArchive);
            var second = TestData.Installed("b", packageKey: null, intrinsicId: "com.example.local.identity", source: ModSourceType.LocalArchive);
            var profile = new ModProfile
            {
                Name = "Local Profile",
                ExpectedMods = new List<ProfileModExpectation> { expectation }
            };

            var item = Assert.Single(new ProfileHealthService().Evaluate(profile, new[] { first, second }).Items);

            Assert.Equal(ProfileHealthStatus.IdentityUncertain, item.Status);
            Assert.Equal(ProfileHealthMatchKind.Ambiguous, item.MatchKind);
        }

        private string CreateSingleAssemblyZip(string name)
        {
            var zip = Path.Combine(_root, name);
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(
                typeof(HTFManager.Tests.Fixtures.LocalIdentityPlugin).Assembly.Location,
                "TrueDotCrosshair.dll",
                CompressionLevel.NoCompression);
            return zip;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }
    }
}
