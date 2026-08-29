using HTFManager.Core.Models;
using HTFManager.Infrastructure.Profiles;

namespace HTFManager.Tests;

public sealed class ProfileHealthServiceTests
{
    private readonly ProfileHealthService _service = new();

    [Fact]
    public void ExactPackageAndVersion_IsHealthy()
    {
        var profile = Profile(TestData.Expectation(resolvedModId: "mod-a"));
        var installed = TestData.Installed("mod-a");

        var item = Assert.Single(_service.Evaluate(profile, new[] { installed }).Items);

        Assert.Equal(ProfileHealthStatus.Healthy, item.Status);
        Assert.Equal(ProfileHealthMatchKind.PackageKey, item.MatchKind);
        Assert.Equal(ProfileHealthReason.None, item.Reason);
    }

    [Fact]
    public void ExactPackageDifferentVersion_IsVersionMismatch()
    {
        var profile = Profile(TestData.Expectation(version: "1.2.0", resolvedModId: "mod-a"));
        var installed = TestData.Installed("mod-a", version: "2.0.0");

        var item = Assert.Single(_service.Evaluate(profile, new[] { installed }).Items);

        Assert.Equal(ProfileHealthStatus.VersionMismatch, item.Status);
        Assert.Equal("1.2.0", item.ExpectedVersion);
        Assert.Equal("2.0.0", item.InstalledVersion);
        Assert.Equal(ProfileHealthReason.ExpectedVersionDiffers, item.Reason);
    }

    [Fact]
    public void TrustedPackageMissing_IsMissingEvenWhenResolvedIdPointsElsewhere()
    {
        var profile = Profile(TestData.Expectation(packageKey: "Author-Expected", resolvedModId: "stale"));
        var stale = TestData.Installed("stale", packageKey: "Author-Other");

        var item = Assert.Single(_service.Evaluate(profile, new[] { stale }).Items);

        Assert.Equal(ProfileHealthStatus.Missing, item.Status);
        Assert.Equal(ProfileHealthMatchKind.None, item.MatchKind);
    }

    [Fact]
    public void TrustedPackageBeatsStaleResolvedId()
    {
        var profile = Profile(TestData.Expectation(packageKey: "Author-Expected", resolvedModId: "stale"));
        var stale = TestData.Installed("stale", packageKey: "Author-Other");
        var correct = TestData.Installed("correct", packageKey: "Author-Expected");

        var item = Assert.Single(_service.Evaluate(profile, new[] { stale, correct }).Items);

        Assert.Equal(ProfileHealthStatus.Healthy, item.Status);
        Assert.Equal("correct", item.InstalledMod?.Id);
        Assert.Equal(ProfileHealthMatchKind.PackageKey, item.MatchKind);
    }

    [Fact]
    public void LocalResolvedIdentity_IsHealthy()
    {
        var expectation = TestData.Expectation(packageKey: null, resolvedModId: "local-a");
        var installed = TestData.Installed(
            "local-a",
            packageKey: null,
            source: ModSourceType.LocalDll,
            managed: true);

        var item = Assert.Single(_service.Evaluate(Profile(expectation), new[] { installed }).Items);

        Assert.Equal(ProfileHealthStatus.Healthy, item.Status);
        Assert.Equal(ProfileHealthMatchKind.ResolvedId, item.MatchKind);
    }

    [Fact]
    public void AmbiguousLocalIdentity_IsIdentityUncertain()
    {
        var expectation = TestData.Expectation(packageKey: null, resolvedModId: null, fileName: "");
        var first = TestData.Installed("a", packageKey: null, fileName: "A.dll", source: ModSourceType.LocalDll);
        var second = TestData.Installed("b", packageKey: null, fileName: "B.dll", source: ModSourceType.LocalDll);

        var item = Assert.Single(_service.Evaluate(Profile(expectation), new[] { first, second }).Items);

        Assert.Equal(ProfileHealthStatus.IdentityUncertain, item.Status);
        Assert.Equal(ProfileHealthMatchKind.Ambiguous, item.MatchKind);
        Assert.Equal(ProfileHealthReason.AmbiguousIdentity, item.Reason);
    }

    [Fact]
    public void RequiredVersionWithUnknownInstalledVersion_IsIdentityUncertain()
    {
        var expectation = TestData.Expectation(version: "1.2.0");
        var installed = TestData.Installed("a", version: "—");

        var item = Assert.Single(_service.Evaluate(Profile(expectation), new[] { installed }).Items);

        Assert.Equal(ProfileHealthStatus.IdentityUncertain, item.Status);
        Assert.Equal(ProfileHealthReason.InstalledVersionUnknown, item.Reason);
    }

    [Fact]
    public void UnknownExpectedVersion_DoesNotCreateVersionMismatch()
    {
        var expectation = TestData.Expectation(version: "—");
        var installed = TestData.Installed("a", version: "9.9.9");

        var item = Assert.Single(_service.Evaluate(Profile(expectation), new[] { installed }).Items);

        Assert.Equal(ProfileHealthStatus.Healthy, item.Status);
    }

    [Fact]
    public void LegacyBindingPresent_IsIdentityUncertain()
    {
        var expectation = TestData.Expectation(
            version: "—",
            packageKey: null,
            resolvedModId: "legacy-id",
            quality: ProfileExpectationMetadataQuality.LegacyBindingOnly);
        var installed = TestData.Installed("legacy-id", packageKey: null, source: ModSourceType.External, managed: false);

        var item = Assert.Single(_service.Evaluate(Profile(expectation), new[] { installed }).Items);

        Assert.Equal(ProfileHealthStatus.IdentityUncertain, item.Status);
        Assert.Equal(ProfileHealthReason.LegacyMetadataUnavailable, item.Reason);
    }

    [Fact]
    public void LegacyBindingMissing_IsMissing()
    {
        var expectation = TestData.Expectation(
            version: "—",
            packageKey: null,
            resolvedModId: "legacy-id",
            quality: ProfileExpectationMetadataQuality.LegacyBindingOnly);

        var item = Assert.Single(_service.Evaluate(Profile(expectation), Array.Empty<InstalledMod>()).Items);

        Assert.Equal(ProfileHealthStatus.Missing, item.Status);
        Assert.Equal(ProfileHealthReason.LegacyBindingMissing, item.Reason);
    }

    private static ModProfile Profile(params ProfileModExpectation[] expectations)
        => new()
        {
            Name = "Test Profile",
            ExpectedMods = expectations.ToList()
        };
}
