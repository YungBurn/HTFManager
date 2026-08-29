namespace HTFManager.Core.Models;

public sealed class ProfileVersionReconciliationItem
{
    public required ProfileHealthItem Health { get; init; }
    public required ProfileVersionReconciliationSource Source { get; init; }
    public PackageArtifact? Artifact { get; init; }
    public HtfBundlePayloadDescriptor? BundlePayload { get; init; }
    public RemoteModPackage? RemotePackage { get; init; }
    public RemoteModVersion? RemoteVersion { get; init; }
    public string Message { get; init; } = "";

    public bool CanRestoreExpected => Health.Status == ProfileHealthStatus.VersionMismatch &&
                                      Source is ProfileVersionReconciliationSource.Bundle or
                                          ProfileVersionReconciliationSource.RetainedArtifact or
                                          ProfileVersionReconciliationSource.Thunderstore or
                                          ProfileVersionReconciliationSource.CatalogRequired;

    public bool CanAcceptInstalled => Health.Status == ProfileHealthStatus.VersionMismatch &&
                                      Health.InstalledMod is not null &&
                                      !VersionUnknown(Health.InstalledMod.Version) &&
                                      HasDeterministicIdentity(Health.Expectation.Requirement);

    private static bool VersionUnknown(string? version)
        => string.IsNullOrWhiteSpace(version) || version.Trim() == "—";

    private static bool HasDeterministicIdentity(ProfileModRequirement requirement)
        => !string.IsNullOrWhiteSpace(requirement.PackageKey) ||
           !string.IsNullOrWhiteSpace(requirement.IntrinsicId);
}
