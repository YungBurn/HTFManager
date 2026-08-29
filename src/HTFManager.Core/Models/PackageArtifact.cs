namespace HTFManager.Core.Models;

public sealed class PackageArtifact
{
    public string RegistryId { get; init; } = "";
    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Length { get; init; }
    public HtfBundleArtifactKind Kind { get; init; }
}
