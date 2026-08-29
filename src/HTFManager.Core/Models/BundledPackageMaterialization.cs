namespace HTFManager.Core.Models;

public sealed class BundledPackageMaterialization
{
    public string SourcePath { get; init; } = "";
    public string TemporaryDirectory { get; init; } = "";
    public ModInstallMetadata Metadata { get; init; } = new();
}
