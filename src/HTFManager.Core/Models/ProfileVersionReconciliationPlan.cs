namespace HTFManager.Core.Models;

public sealed class ProfileVersionReconciliationPlan
{
    public string ProfileName { get; init; } = "Profile";
    public IReadOnlyList<ProfileVersionReconciliationItem> Items { get; init; } = Array.Empty<ProfileVersionReconciliationItem>();

    public int RestorableCount => Items.Count(item => item.CanRestoreExpected);
    public int AcceptableCount => Items.Count(item => item.CanAcceptInstalled);
    public int ManualCount => Items.Count(item => item.Source == ProfileVersionReconciliationSource.Manual);
}
