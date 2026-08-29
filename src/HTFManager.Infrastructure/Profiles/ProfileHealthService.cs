using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Profiles;

public sealed class ProfileHealthService : IProfileHealthService
{
    public ProfileHealthReport Evaluate(ModProfile profile, IReadOnlyList<InstalledMod> installedMods)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(installedMods);

        var items = profile.ExpectedMods
            .Select(expectation => Evaluate(expectation, installedMods))
            .ToArray();

        return new ProfileHealthReport
        {
            ProfileName = profile.Name,
            Items = items
        };
    }

    private static ProfileHealthItem Evaluate(
        ProfileModExpectation expectation,
        IReadOnlyList<InstalledMod> installedMods)
    {
        if (expectation.MetadataQuality == ProfileExpectationMetadataQuality.LegacyBindingOnly)
            return EvaluateLegacy(expectation, installedMods);

        var requirement = expectation.Requirement;
        if (!string.IsNullOrWhiteSpace(requirement.PackageKey))
        {
            var packageMatches = installedMods
                .Where(mod => !string.IsNullOrWhiteSpace(mod.PackageKey) &&
                              mod.PackageKey!.Equals(requirement.PackageKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return packageMatches.Length switch
            {
                0 => Missing(expectation, ProfileHealthReason.ExpectedIdentityNotInstalled),
                1 => EvaluateVersion(expectation, packageMatches[0], ProfileHealthMatchKind.PackageKey),
                _ => Uncertain(expectation, null, ProfileHealthMatchKind.Ambiguous, ProfileHealthReason.AmbiguousIdentity)
            };
        }

        if (!string.IsNullOrWhiteSpace(requirement.IntrinsicId))
        {
            var intrinsicMatches = installedMods
                .Where(mod => string.IsNullOrWhiteSpace(mod.PackageKey))
                .Where(mod => !string.IsNullOrWhiteSpace(mod.IntrinsicId) &&
                              mod.IntrinsicId!.Equals(requirement.IntrinsicId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return intrinsicMatches.Length switch
            {
                0 => Missing(expectation, ProfileHealthReason.ExpectedIdentityNotInstalled),
                1 => EvaluateVersion(expectation, intrinsicMatches[0], ProfileHealthMatchKind.IntrinsicId),
                _ => Uncertain(expectation, null, ProfileHealthMatchKind.Ambiguous, ProfileHealthReason.AmbiguousIdentity)
            };
        }

        if (!string.IsNullOrWhiteSpace(expectation.ResolvedModId))
        {
            var resolved = installedMods.FirstOrDefault(mod =>
                mod.Id.Equals(expectation.ResolvedModId, StringComparison.OrdinalIgnoreCase));
            if (resolved is not null && LocalIdentityCompatible(requirement, resolved))
                return EvaluateVersion(expectation, resolved, ProfileHealthMatchKind.ResolvedId);
        }

        var fileMatches = FindFileMatches(requirement, installedMods);
        if (fileMatches.Length == 1)
            return EvaluateVersion(expectation, fileMatches[0], ProfileHealthMatchKind.LocalIdentity);
        if (fileMatches.Length > 1)
            return Uncertain(expectation, null, ProfileHealthMatchKind.Ambiguous, ProfileHealthReason.AmbiguousIdentity);

        var nameMatches = FindNameMatches(requirement, installedMods);
        if (nameMatches.Length == 1)
            return EvaluateVersion(expectation, nameMatches[0], ProfileHealthMatchKind.LocalIdentity);
        if (nameMatches.Length > 1)
            return Uncertain(expectation, null, ProfileHealthMatchKind.Ambiguous, ProfileHealthReason.AmbiguousIdentity);

        return Missing(expectation, ProfileHealthReason.ExpectedIdentityNotInstalled);
    }

    private static ProfileHealthItem EvaluateLegacy(
        ProfileModExpectation expectation,
        IReadOnlyList<InstalledMod> installedMods)
    {
        if (!string.IsNullOrWhiteSpace(expectation.ResolvedModId))
        {
            var bound = installedMods.FirstOrDefault(mod =>
                mod.Id.Equals(expectation.ResolvedModId, StringComparison.OrdinalIgnoreCase));
            if (bound is not null)
            {
                return Uncertain(
                    expectation,
                    bound,
                    ProfileHealthMatchKind.ResolvedId,
                    ProfileHealthReason.LegacyMetadataUnavailable);
            }
        }

        return Missing(expectation, ProfileHealthReason.LegacyBindingMissing);
    }

    private static ProfileHealthItem EvaluateVersion(
        ProfileModExpectation expectation,
        InstalledMod installed,
        ProfileHealthMatchKind matchKind)
    {
        var expectedVersion = expectation.Requirement.Version;
        if (VersionUnknown(expectedVersion))
        {
            return new ProfileHealthItem
            {
                Expectation = expectation,
                InstalledMod = installed,
                Status = ProfileHealthStatus.Healthy,
                MatchKind = matchKind,
                Reason = ProfileHealthReason.None
            };
        }

        if (VersionUnknown(installed.Version))
            return Uncertain(expectation, installed, matchKind, ProfileHealthReason.InstalledVersionUnknown);

        if (expectedVersion.Equals(installed.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileHealthItem
            {
                Expectation = expectation,
                InstalledMod = installed,
                Status = ProfileHealthStatus.Healthy,
                MatchKind = matchKind,
                Reason = ProfileHealthReason.None
            };
        }

        return new ProfileHealthItem
        {
            Expectation = expectation,
            InstalledMod = installed,
            Status = ProfileHealthStatus.VersionMismatch,
            MatchKind = matchKind,
            Reason = ProfileHealthReason.ExpectedVersionDiffers
        };
    }

    private static ProfileHealthItem Missing(ProfileModExpectation expectation, ProfileHealthReason reason)
        => new()
        {
            Expectation = expectation,
            Status = ProfileHealthStatus.Missing,
            MatchKind = ProfileHealthMatchKind.None,
            Reason = reason
        };

    private static ProfileHealthItem Uncertain(
        ProfileModExpectation expectation,
        InstalledMod? installed,
        ProfileHealthMatchKind matchKind,
        ProfileHealthReason reason)
        => new()
        {
            Expectation = expectation,
            InstalledMod = installed,
            Status = ProfileHealthStatus.IdentityUncertain,
            MatchKind = matchKind,
            Reason = reason
        };

    private static InstalledMod[] FindFileMatches(
        ProfileModRequirement requirement,
        IReadOnlyList<InstalledMod> installedMods)
    {
        if (string.IsNullOrWhiteSpace(requirement.FileName))
            return Array.Empty<InstalledMod>();

        return installedMods
            .Where(mod => LoaderAndComponentCompatible(requirement, mod))
            .Where(mod => NormalizeFileName(mod.FilePath).Equals(requirement.FileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static InstalledMod[] FindNameMatches(
        ProfileModRequirement requirement,
        IReadOnlyList<InstalledMod> installedMods)
    {
        if (string.IsNullOrWhiteSpace(requirement.Name) || requirement.Name.Equals("Unknown Mod", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<InstalledMod>();

        return installedMods
            .Where(mod => LoaderAndComponentCompatible(requirement, mod))
            .Where(mod => mod.Name.Equals(requirement.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static bool LocalIdentityCompatible(ProfileModRequirement requirement, InstalledMod installed)
    {
        if (!LoaderAndComponentCompatible(requirement, installed))
            return false;
        if (!string.IsNullOrWhiteSpace(requirement.IntrinsicId))
            return !string.IsNullOrWhiteSpace(installed.IntrinsicId) &&
                   installed.IntrinsicId!.Equals(requirement.IntrinsicId, StringComparison.OrdinalIgnoreCase);

        var hasName = !string.IsNullOrWhiteSpace(requirement.Name) &&
                      !requirement.Name.Equals("Unknown Mod", StringComparison.OrdinalIgnoreCase);
        var hasFile = !string.IsNullOrWhiteSpace(requirement.FileName);

        if (hasName && !installed.Name.Equals(requirement.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (hasFile && !NormalizeFileName(installed.FilePath).Equals(requirement.FileName, StringComparison.OrdinalIgnoreCase))
            return false;

        return hasName || hasFile;
    }

    private static bool LoaderAndComponentCompatible(ProfileModRequirement requirement, InstalledMod installed)
        => (requirement.Loader == ModLoaderKind.Unknown || installed.Loader == requirement.Loader) &&
           (requirement.Component == ModComponentKind.Unknown || installed.Component == requirement.Component);

    private static bool VersionUnknown(string? version)
        => string.IsNullOrWhiteSpace(version) || version == "—";

    private static string NormalizeFileName(string path)
        => Path.GetFileName(path).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase);
}
