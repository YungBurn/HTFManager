using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Profiles;

namespace HTFManager.Tests;

public sealed class ProfileVersionReconciliationTests
{
    [Fact]
    public void BuildPlan_PrefersExactBundleOverRetainedAndRemote()
    {
        var profile = MismatchProfile(out var installed);
        var requirement = profile.ExpectedMods.Single().Requirement;
        var artifacts = new StubArtifacts
        {
            Exact = new PackageArtifact
            {
                Path = "retained.zip",
                FileName = "retained.zip",
                Version = "1.0.0",
                PackageKey = requirement.PackageKey,
                Source = ModSourceType.Thunderstore
            }
        };
        var bundle = new HtfBundlePayloadDescriptor
        {
            PortableId = requirement.PortableId,
            PackageKey = requirement.PackageKey,
            IntrinsicId = requirement.IntrinsicId,
            Version = requirement.Version,
            Source = requirement.Source,
            Entry = "payload/a/package.zip",
            Sha256 = new string('A', 64),
            UncompressedSize = 1
        };
        var remote = RemotePackage("1.0.0");
        var service = new ProfileVersionReconciliationService(new ProfileHealthService(), artifacts);

        var item = Assert.Single(service.BuildPlan(profile, new[] { installed }, new[] { remote }, new[] { bundle }).Items);

        Assert.Equal(ProfileVersionReconciliationSource.Bundle, item.Source);
        Assert.Same(bundle, item.BundlePayload);
        Assert.True(item.CanRestoreExpected);
        Assert.True(item.CanAcceptInstalled);
    }

    [Fact]
    public void BuildPlan_UsesRetainedExactArtifactBeforeThunderstore()
    {
        var profile = MismatchProfile(out var installed);
        var artifact = new PackageArtifact
        {
            Path = "retained.zip",
            FileName = "retained.zip",
            Version = "1.0.0",
            PackageKey = "Author-ExampleMod",
            Source = ModSourceType.Thunderstore
        };
        var service = new ProfileVersionReconciliationService(new ProfileHealthService(), new StubArtifacts { Exact = artifact });

        var item = Assert.Single(service.BuildPlan(profile, new[] { installed }, new[] { RemotePackage("1.0.0") }).Items);

        Assert.Equal(ProfileVersionReconciliationSource.RetainedArtifact, item.Source);
        Assert.Same(artifact, item.Artifact);
    }

    [Fact]
    public void BuildPlan_UsesExactThunderstoreVersionWithoutFallback()
    {
        var profile = MismatchProfile(out var installed);
        var remote = RemotePackage("1.0.0", "1.5.0");
        var service = new ProfileVersionReconciliationService(new ProfileHealthService(), new StubArtifacts());

        var item = Assert.Single(service.BuildPlan(profile, new[] { installed }, new[] { remote }).Items);

        Assert.Equal(ProfileVersionReconciliationSource.Thunderstore, item.Source);
        Assert.Equal("1.0.0", item.RemoteVersion!.VersionNumber);
    }

    [Fact]
    public void BuildPlan_DoesNotUseDifferentRemoteVersionAsFallback()
    {
        var profile = MismatchProfile(out var installed);
        var service = new ProfileVersionReconciliationService(new ProfileHealthService(), new StubArtifacts());

        var item = Assert.Single(service.BuildPlan(profile, new[] { installed }, new[] { RemotePackage("1.5.0") }).Items);

        Assert.Equal(ProfileVersionReconciliationSource.Manual, item.Source);
        Assert.False(item.CanRestoreExpected);
    }

    [Fact]
    public void BuildPlan_RequestsCatalogOnlyForThunderstoreIdentity()
    {
        var profile = MismatchProfile(out var installed);
        var service = new ProfileVersionReconciliationService(new ProfileHealthService(), new StubArtifacts());

        var item = Assert.Single(service.BuildPlan(profile, new[] { installed }, Array.Empty<RemoteModPackage>(), catalogAvailable: false).Items);

        Assert.Equal(ProfileVersionReconciliationSource.CatalogRequired, item.Source);
        Assert.True(item.CanRestoreExpected);
    }

    [Fact]
    public void BuildPlan_DoesNotOfferAcceptInstalledWhenLogicalSourceDiffers()
    {
        var profile = MismatchProfile(out _);
        profile.ExpectedMods.Single().Requirement.Source = ModSourceType.Thunderstore;
        var installed = TestData.Installed("installed", version: "2.0.0", source: ModSourceType.LocalArchive);
        var service = new ProfileVersionReconciliationService(new ProfileHealthService(), new StubArtifacts());

        var item = Assert.Single(service.BuildPlan(profile, new[] { installed }, Array.Empty<RemoteModPackage>()).Items);

        Assert.Equal(ProfileHealthStatus.VersionMismatch, item.Health.Status);
        Assert.False(item.InstalledSourceMatchesExpectation);
        Assert.False(item.CanAcceptInstalled);
    }

    private static ModProfile MismatchProfile(out InstalledMod installed)
    {
        installed = TestData.Installed("installed", version: "2.0.0");
        return new ModProfile
        {
            Name = "Profile",
            ExpectedMods = new List<ProfileModExpectation>
            {
                TestData.Expectation(version: "1.0.0", resolvedModId: installed.Id)
            }
        };
    }

    private static RemoteModPackage RemotePackage(params string[] versions)
        => new()
        {
            Name = "ExampleMod",
            FullName = "Author-ExampleMod",
            Owner = "Author",
            Versions = versions.Select(version => new RemoteModVersion
            {
                VersionNumber = version,
                DownloadUrl = "https://example.invalid/" + version + ".zip",
                IsActive = true,
                DateCreated = DateTimeOffset.UtcNow
            }).ToList()
        };

    private sealed class StubArtifacts : IPackageArtifactStore
    {
        public PackageArtifact? Exact { get; init; }
        public PackageArtifact? FindVerifiedArtifact(InstalledMod mod) => null;
        public PackageArtifact? FindExactArtifact(ProfileModRequirement requirement) => Exact;
        public void PreserveCurrentArtifact(ModInstallationRecord record) { }
        public void CaptureArtifact(ModInstallationRecord record, string sourcePath) { }
    }
}
