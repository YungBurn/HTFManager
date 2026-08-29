namespace HTFManager.Core.Models;

public sealed class ModInstallationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameDirectory { get; set; } = "";
    public string Name { get; set; } = "Unknown Mod";
    public string Version { get; set; } = "—";
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = "";
    public string? PackageKey { get; set; }
    public string? IntrinsicId { get; set; }
    public ModSourceType Source { get; set; } = ModSourceType.LocalArchive;
    public ModPackageKind Kind { get; set; } = ModPackageKind.Unknown;
    public ModLoaderKind Loader { get; set; } = ModLoaderKind.Unknown;
    public ModComponentKind Component { get; set; } = ModComponentKind.Unknown;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public string? SourceFileName { get; set; }
    public string? SourceHash { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public List<string> Files { get; set; } = new();
    public List<string> UserDataFiles { get; set; } = new();
}
