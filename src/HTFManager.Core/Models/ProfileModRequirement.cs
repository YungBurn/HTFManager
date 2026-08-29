namespace HTFManager.Core.Models;

public sealed class ProfileModRequirement
{
    public string PortableId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Unknown Mod";
    public string Version { get; set; } = "—";
    public string Author { get; set; } = "";
    public string? PackageKey { get; set; }
    public string? IntrinsicId { get; set; }
    public string FileName { get; set; } = "";
    public ModSourceType Source { get; set; } = ModSourceType.External;
    public ModLoaderKind Loader { get; set; } = ModLoaderKind.Unknown;
    public ModComponentKind Component { get; set; } = ModComponentKind.Unknown;
    public bool Enabled { get; set; }
}
