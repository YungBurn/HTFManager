namespace HTFManager.Core.Models;

public sealed class ProfileBundleExportPlan
{
    public string ProfileName { get; init; } = "Profile";
    public IReadOnlyList<ProfileBundleExportItem> Items { get; init; } = Array.Empty<ProfileBundleExportItem>();

    public int BundledCount => Items.Count(item => item.Disposition == ProfileBundleExportDisposition.Bundled);
    public int RemoteOnlyCount => Items.Count(item => item.Disposition == ProfileBundleExportDisposition.RemoteOnly);
    public int ManualCount => Items.Count(item => item.Disposition == ProfileBundleExportDisposition.Manual);
    public int VersionDriftCount => Items.Count(item => item.Disposition == ProfileBundleExportDisposition.VersionDrift);
    public long EstimatedPayloadBytes => Items.Where(item => item.Artifact is not null).Sum(item => item.Artifact!.Length);
}
