namespace HTFManager.Core.Models;

public sealed class ProfileHealthReport
{
    public string ProfileName { get; init; } = "Profile";
    public IReadOnlyList<ProfileHealthItem> Items { get; init; } = Array.Empty<ProfileHealthItem>();

    public int HealthyCount => Items.Count(item => item.Status == ProfileHealthStatus.Healthy);
    public int MissingCount => Items.Count(item => item.Status == ProfileHealthStatus.Missing);
    public int VersionMismatchCount => Items.Count(item => item.Status == ProfileHealthStatus.VersionMismatch);
    public int IdentityUncertainCount => Items.Count(item => item.Status == ProfileHealthStatus.IdentityUncertain);
    public bool HasBlockingMissing => MissingCount > 0;
}
