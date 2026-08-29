namespace HTFManager.Core.Models;

public sealed class PackageArtifactRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? PackageKey { get; set; }
    public string? IntrinsicId { get; set; }
    public string Version { get; set; } = "—";
    public ModSourceType Source { get; set; } = ModSourceType.LocalArchive;
    public HtfBundleArtifactKind Kind { get; set; }
    public string FileName { get; set; } = "package.zip";
    public string Sha256 { get; set; } = "";
    public long Length { get; set; }
    public string StoredPath { get; set; } = "";
    public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;
}
