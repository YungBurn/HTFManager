namespace HTFManager.Core.Models;

public sealed class ProfileConfigurationSnapshot
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string SnapshotFileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public ModLoaderKind Loader { get; set; } = ModLoaderKind.Unknown;
    public string? AssociatedModId { get; set; }
    public string? AssociatedPortableModId { get; set; }
    public DateTime CapturedUtc { get; set; }
}
