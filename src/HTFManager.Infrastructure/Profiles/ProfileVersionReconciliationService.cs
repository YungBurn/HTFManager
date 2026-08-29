using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Profiles;

public sealed class ProfileVersionReconciliationService : IProfileVersionReconciliationService
{
    private readonly IProfileHealthService _health;
    private readonly IPackageArtifactStore _artifacts;

    public ProfileVersionReconciliationService(IProfileHealthService health, IPackageArtifactStore artifacts)
    {
        _health = health;
        _artifacts = artifacts;
    }

    public ProfileVersionReconciliationPlan BuildPlan(
        ModProfile profile,
        IReadOnlyList<InstalledMod> installedMods,
        IReadOnlyList<RemoteModPackage> catalog,
        IReadOnlyList<HtfBundlePayloadDescriptor>? bundledPayloads = null,
        bool catalogAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(installedMods);
        ArgumentNullException.ThrowIfNull(catalog);

        bundledPayloads ??= Array.Empty<HtfBundlePayloadDescriptor>();
        var health = _health.Evaluate(profile, installedMods);
        var bundleByPortableId = bundledPayloads
            .GroupBy(payload => payload.PortableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var packagesByKey = catalog
            .Where(package => !string.IsNullOrWhiteSpace(package.FullName))
            .GroupBy(package => package.FullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var items = health.Items
            .Where(item => item.Status == ProfileHealthStatus.VersionMismatch)
            .Select(item => BuildItem(item, bundleByPortableId, packagesByKey, catalogAvailable))
            .ToArray();

        return new ProfileVersionReconciliationPlan
        {
            ProfileName = profile.Name,
            Items = items
        };
    }

    private ProfileVersionReconciliationItem BuildItem(
        ProfileHealthItem health,
        IReadOnlyDictionary<string, HtfBundlePayloadDescriptor> bundleByPortableId,
        IReadOnlyDictionary<string, RemoteModPackage> packagesByKey,
        bool catalogAvailable)
    {
        var requirement = health.Expectation.Requirement;
        if (VersionUnknown(requirement.Version) || !HasDeterministicIdentity(requirement))
            return Manual(health, "The expected version or deterministic identity is unavailable.");

        if (bundleByPortableId.TryGetValue(requirement.PortableId, out var payload) && PayloadMatches(payload, requirement))
        {
            return new ProfileVersionReconciliationItem
            {
                Health = health,
                Source = ProfileVersionReconciliationSource.Bundle,
                BundlePayload = payload,
                Message = $"Exact expected version {requirement.Version} is available in the imported portable bundle."
            };
        }

        var artifact = _artifacts.FindExactArtifact(requirement);
        if (artifact is not null)
        {
            return new ProfileVersionReconciliationItem
            {
                Health = health,
                Source = ProfileVersionReconciliationSource.RetainedArtifact,
                Artifact = artifact,
                Message = $"Exact expected version {requirement.Version} is available in the retained package history."
            };
        }

        if (!string.IsNullOrWhiteSpace(requirement.PackageKey) && requirement.Source == ModSourceType.Thunderstore)
        {
            if (!catalogAvailable)
            {
                return new ProfileVersionReconciliationItem
                {
                    Health = health,
                    Source = ProfileVersionReconciliationSource.CatalogRequired,
                    Message = "Thunderstore must be checked for the exact expected version."
                };
            }

            if (packagesByKey.TryGetValue(requirement.PackageKey.Trim(), out var package))
            {
                var version = package.Versions.FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.DownloadUrl) &&
                    candidate.VersionNumber.Trim().Equals(requirement.Version.Trim(), StringComparison.OrdinalIgnoreCase));
                if (version is not null)
                {
                    return new ProfileVersionReconciliationItem
                    {
                        Health = health,
                        Source = ProfileVersionReconciliationSource.Thunderstore,
                        RemotePackage = package,
                        RemoteVersion = version,
                        Message = $"Exact expected version {version.VersionNumber} is downloadable from Thunderstore."
                    };
                }
            }
        }

        return Manual(health, $"Exact expected version {requirement.Version} is not available from a retained artifact or configured provider.");
    }

    private static ProfileVersionReconciliationItem Manual(ProfileHealthItem health, string message)
        => new()
        {
            Health = health,
            Source = ProfileVersionReconciliationSource.Manual,
            Message = message
        };

    private static bool PayloadMatches(HtfBundlePayloadDescriptor payload, ProfileModRequirement requirement)
        => payload.PortableId.Equals(requirement.PortableId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(payload.PackageKey?.Trim() ?? "", requirement.PackageKey?.Trim() ?? "", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(payload.IntrinsicId?.Trim() ?? "", requirement.IntrinsicId?.Trim() ?? "", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(payload.Version.Trim(), requirement.Version.Trim(), StringComparison.OrdinalIgnoreCase) &&
           payload.Source == requirement.Source &&
           HasDeterministicIdentity(requirement);

    private static bool HasDeterministicIdentity(ProfileModRequirement requirement)
        => !string.IsNullOrWhiteSpace(requirement.PackageKey) || !string.IsNullOrWhiteSpace(requirement.IntrinsicId);

    private static bool VersionUnknown(string? version)
        => string.IsNullOrWhiteSpace(version) || version.Trim() == "—";
}
