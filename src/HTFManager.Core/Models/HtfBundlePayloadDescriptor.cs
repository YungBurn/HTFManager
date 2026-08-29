namespace HTFManager.Core.Models;

public sealed class HtfBundlePayloadDescriptor
{
    public string PortableId { get; set; } = "";
    public string? PackageKey { get; set; }
    public string? IntrinsicId { get; set; }
    public string Version { get; set; } = "—";
    public ModSourceType Source { get; set; } = ModSourceType.External;
    public HtfBundleArtifactKind ArtifactKind { get; set; } = HtfBundleArtifactKind.Archive;
    public string Entry { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long UncompressedSize { get; set; }
}
