namespace HTFManager.Core.Models;

public sealed class ProfileBundleExportItem
{
    public required ProfileModExpectation Expectation { get; init; }
    public required ProfileHealthItem Health { get; init; }
    public InstalledMod? InstalledMod { get; init; }
    public ProfileBundleExportDisposition Disposition { get; init; }
    public PackageArtifact? Artifact { get; init; }
    public string Reason { get; init; } = "";
}
