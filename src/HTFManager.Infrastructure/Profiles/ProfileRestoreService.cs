using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Profiles;

public sealed class ProfileRestoreService : IProfileRestoreService
{
    public ProfileRestorePlan BuildPlan(ModProfile profile, IReadOnlyList<RemoteModPackage> catalog)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(catalog);

        var packagesByKey = catalog
            .Where(package => !string.IsNullOrWhiteSpace(package.FullName))
            .GroupBy(package => package.FullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var items = profile.UnresolvedMods
            .Select(requirement => BuildItem(requirement, packagesByKey))
            .ToArray();

        return new ProfileRestorePlan
        {
            ProfileName = profile.Name,
            Items = items
        };
    }

    private static ProfileRestoreItem BuildItem(
        ProfileModRequirement requirement,
        IReadOnlyDictionary<string, RemoteModPackage> packagesByKey)
    {
        if (requirement.Source != ModSourceType.Thunderstore)
        {
            return Manual(
                requirement,
                $"Automatic restore is not available for source '{requirement.Source}'. Restore this requirement manually.");
        }

        var packageKey = requirement.PackageKey?.Trim();
        if (string.IsNullOrWhiteSpace(packageKey))
        {
            return Manual(
                requirement,
                "This Thunderstore requirement has no package key. Automatic restore requires an exact PackageKey match.");
        }

        if (!packagesByKey.TryGetValue(packageKey, out var package))
        {
            return new ProfileRestoreItem
            {
                Requirement = requirement,
                Disposition = ProfileRestoreDisposition.PackageUnavailable,
                Message = $"Thunderstore package '{packageKey}' was not found in the current catalog."
            };
        }

        var requestedVersion = NormalizeRequestedVersion(requirement.Version);
        if (requestedVersion is not null)
        {
            var exactVersion = package.Versions.FirstOrDefault(version =>
                HasDownload(version) &&
                version.VersionNumber.Trim().Equals(requestedVersion, StringComparison.OrdinalIgnoreCase));

            if (exactVersion is not null)
            {
                return new ProfileRestoreItem
                {
                    Requirement = requirement,
                    Disposition = ProfileRestoreDisposition.Ready,
                    RemotePackage = package,
                    SelectedVersion = exactVersion,
                    Message = BuildExactVersionMessage(package, exactVersion)
                };
            }
        }

        var fallback = SelectLatestDownloadableVersion(package);
        if (fallback is null)
        {
            return new ProfileRestoreItem
            {
                Requirement = requirement,
                Disposition = ProfileRestoreDisposition.PackageUnavailable,
                RemotePackage = package,
                Message = requestedVersion is null
                    ? $"Thunderstore package '{packageKey}' has no downloadable versions in the current catalog."
                    : $"Requested version {requestedVersion} is unavailable and package '{packageKey}' has no downloadable fallback version."
            };
        }

        if (requestedVersion is null)
        {
            return new ProfileRestoreItem
            {
                Requirement = requirement,
                Disposition = ProfileRestoreDisposition.Ready,
                RemotePackage = package,
                SelectedVersion = fallback,
                Message = BuildNoRequestedVersionMessage(package, fallback)
            };
        }

        return new ProfileRestoreItem
        {
            Requirement = requirement,
            Disposition = ProfileRestoreDisposition.VersionFallback,
            RemotePackage = package,
            SelectedVersion = fallback,
            Message = BuildFallbackMessage(package, requestedVersion, fallback)
        };
    }

    private static ProfileRestoreItem Manual(ProfileModRequirement requirement, string message)
        => new()
        {
            Requirement = requirement,
            Disposition = ProfileRestoreDisposition.ManualRequired,
            Message = message
        };

    private static RemoteModVersion? SelectLatestDownloadableVersion(RemoteModPackage package)
        => package.Versions
               .Where(version => version.IsActive && HasDownload(version))
               .OrderByDescending(version => version.DateCreated)
               .FirstOrDefault()
           ?? package.Versions
               .Where(HasDownload)
               .OrderByDescending(version => version.DateCreated)
               .FirstOrDefault();

    private static bool HasDownload(RemoteModVersion version)
        => !string.IsNullOrWhiteSpace(version.DownloadUrl);

    private static string? NormalizeRequestedVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var normalized = version.Trim();
        return normalized == "—" ? null : normalized;
    }

    private static string BuildExactVersionMessage(RemoteModPackage package, RemoteModVersion version)
    {
        var warnings = new List<string>();
        if (package.IsDeprecated)
            warnings.Add("the package is marked deprecated");
        if (!version.IsActive)
            warnings.Add("the requested version is marked inactive");

        return warnings.Count == 0
            ? $"Exact requested version {version.VersionNumber} is available."
            : $"Exact requested version {version.VersionNumber} is available, but {string.Join(" and ", warnings)}.";
    }

    private static string BuildNoRequestedVersionMessage(RemoteModPackage package, RemoteModVersion version)
        => $"No requested version was recorded; selected version {version.VersionNumber}.{BuildSelectionWarningSuffix(package, version)}";

    private static string BuildFallbackMessage(RemoteModPackage package, string requestedVersion, RemoteModVersion fallback)
        => $"Requested version {requestedVersion} is not downloadable; selected fallback version {fallback.VersionNumber}.{BuildSelectionWarningSuffix(package, fallback)}";

    private static string BuildSelectionWarningSuffix(RemoteModPackage package, RemoteModVersion version)
    {
        var warnings = new List<string>();
        if (package.IsDeprecated)
            warnings.Add("the package is marked deprecated");
        if (!version.IsActive)
            warnings.Add("the selected version is marked inactive");

        return warnings.Count == 0
            ? ""
            : " Warning: " + string.Join(" and ", warnings) + ".";
    }
}
