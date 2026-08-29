namespace HTFManager.Core.Models;

public sealed class ProfileBundleInspectionItem
{
    public required ProfileHealthItem Health { get; init; }
    public HtfBundlePayloadDescriptor? BundledPayload { get; init; }
    public bool HasBundledExact => Health.Status == ProfileHealthStatus.Missing && BundledPayload is not null;
}
