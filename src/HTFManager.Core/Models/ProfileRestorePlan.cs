namespace HTFManager.Core.Models;

public sealed class ProfileRestorePlan
{
    public string ProfileName { get; init; } = "";
    public IReadOnlyList<ProfileRestoreItem> Items { get; init; } = Array.Empty<ProfileRestoreItem>();

    public int TotalCount => Items.Count;
    public int ReadyCount => Items.Count(item => item.Disposition == ProfileRestoreDisposition.Ready);
    public int VersionFallbackCount => Items.Count(item => item.Disposition == ProfileRestoreDisposition.VersionFallback);
    public int PackageUnavailableCount => Items.Count(item => item.Disposition is ProfileRestoreDisposition.PackageUnavailable or ProfileRestoreDisposition.CatalogUnavailable);
    public int ManualRequiredCount => Items.Count(item => item.Disposition == ProfileRestoreDisposition.ManualRequired);
    public int InstallableCount => ReadyCount + VersionFallbackCount;
    public bool IsComplete => Items.Count == 0;
    public bool HasInstallableItems => InstallableCount > 0;
}
