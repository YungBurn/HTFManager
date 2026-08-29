using HTFManager.Core.Models;
using HTFManager.Infrastructure.Profiles;

namespace HTFManager.Tests;

public sealed class ProfileRestoreBundleTests
{
    [Fact]
    public void BuildPlan_PrefersExactBundledPayloadBeforeRemoteCatalog()
    {
        var requirement = TestData.Expectation(version: "1.2.0").Requirement;
        var profile = new ModProfile
        {
            Name = "Shared",
            UnresolvedMods = new List<ProfileModRequirement> { requirement }
        };
        var payload = new HtfBundlePayloadDescriptor
        {
            PortableId = requirement.PortableId,
            PackageKey = requirement.PackageKey,
            Version = requirement.Version,
            Source = requirement.Source,
            ArtifactKind = HtfBundleArtifactKind.Archive,
            Entry = "payload/a/package.zip",
            Sha256 = new string('A', 64),
            UncompressedSize = 10
        };

        var plan = new ProfileRestoreService().BuildPlan(
            profile,
            Array.Empty<RemoteModPackage>(),
            new[] { payload },
            catalogAvailable: false);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ProfileRestoreDisposition.Ready, item.Disposition);
        Assert.Equal(ProfileRestoreSource.Bundle, item.RestoreSource);
        Assert.Same(payload, item.BundlePayload);
        Assert.Null(item.RemotePackage);
    }

    [Fact]
    public void BuildPlan_UsesExactBundledLocalPayloadWithIntrinsicIdentity()
    {
        var requirement = TestData.Expectation(
            version: "1.2.3",
            packageKey: null,
            intrinsicId: "com.example.local.identity").Requirement;
        requirement.Source = ModSourceType.LocalArchive;
        var profile = new ModProfile
        {
            Name = "Local Shared",
            UnresolvedMods = new List<ProfileModRequirement> { requirement }
        };
        var payload = new HtfBundlePayloadDescriptor
        {
            PortableId = requirement.PortableId,
            IntrinsicId = requirement.IntrinsicId,
            Version = requirement.Version,
            Source = requirement.Source,
            ArtifactKind = HtfBundleArtifactKind.Archive,
            Entry = "payload/local/package.zip",
            Sha256 = new string('A', 64),
            UncompressedSize = 10
        };

        var item = Assert.Single(new ProfileRestoreService().BuildPlan(
            profile,
            Array.Empty<RemoteModPackage>(),
            new[] { payload },
            catalogAvailable: false).Items);

        Assert.Equal(ProfileRestoreDisposition.Ready, item.Disposition);
        Assert.Equal(ProfileRestoreSource.Bundle, item.RestoreSource);
        Assert.Same(payload, item.BundlePayload);
    }

    [Fact]
    public void BuildPlan_DoesNotUseBundledPayloadWithWrongVersion()
    {
        var requirement = TestData.Expectation(version: "1.2.0").Requirement;
        var profile = new ModProfile
        {
            Name = "Shared",
            UnresolvedMods = new List<ProfileModRequirement> { requirement }
        };
        var payload = new HtfBundlePayloadDescriptor
        {
            PortableId = requirement.PortableId,
            PackageKey = requirement.PackageKey,
            Version = "2.0.0",
            Source = requirement.Source,
            Entry = "payload/a/package.zip",
            Sha256 = new string('A', 64),
            UncompressedSize = 10
        };

        var item = Assert.Single(new ProfileRestoreService().BuildPlan(
            profile,
            Array.Empty<RemoteModPackage>(),
            new[] { payload },
            catalogAvailable: false).Items);

        Assert.Equal(ProfileRestoreDisposition.CatalogUnavailable, item.Disposition);
        Assert.Equal(ProfileRestoreSource.None, item.RestoreSource);
    }
}
