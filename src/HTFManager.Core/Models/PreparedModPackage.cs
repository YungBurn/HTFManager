namespace HTFManager.Core.Models;

public sealed class PreparedModPackage
{
    public string SourcePath { get; init; } = "";
    public ModInstallMetadata? Metadata { get; init; }
    public PackageInspectionResult Inspection { get; init; } = new();
    public RemoteModPackage? RemotePackage { get; init; }
    public string? TemporaryDirectory { get; init; }
    public bool IsVersionReconciliation { get; init; }
}
