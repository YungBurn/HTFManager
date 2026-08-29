namespace HTFManager.Core.Models;

public sealed class ProfileBundleInspection
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public string BundlePath { get; init; } = "";
    public HtfBundleManifest? Manifest { get; init; }
    public ProfilePackageInspection? ProfileInspection { get; init; }
    public ProfileHealthReport? Health { get; init; }
    public IReadOnlyList<ProfileBundleInspectionItem> Items { get; init; } = Array.Empty<ProfileBundleInspectionItem>();

    public int HealthyCount => Health?.HealthyCount ?? 0;
    public int VersionMismatchCount => Health?.VersionMismatchCount ?? 0;
    public int IdentityUncertainCount => Health?.IdentityUncertainCount ?? 0;
    public int BundledMissingCount => Items.Count(item => item.HasBundledExact);
    public int UnbundledMissingCount => Items.Count(item => item.Health.Status == ProfileHealthStatus.Missing && item.BundledPayload is null);

    public static ProfileBundleInspection Invalid(string path, string error)
        => new() { IsValid = false, BundlePath = path, Error = error };
}
