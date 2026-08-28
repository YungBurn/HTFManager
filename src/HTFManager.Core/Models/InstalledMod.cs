namespace HTFManager.Core.Models;

public sealed class InstalledMod
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public string Version { get; init; } = "—";
    public string Author { get; init; } = "Local / External";
    public string Description { get; init; } = "";
    public bool Enabled { get; init; }
    public bool IsExternal { get; init; } = true;
    public bool IsManaged { get; init; }
    public string? RegistryId { get; init; }
    public string? PackageKey { get; init; }
    public ModSourceType Source { get; init; } = ModSourceType.External;
    public ModPackageKind Kind { get; init; } = ModPackageKind.Unknown;
    public ModLoaderKind Loader { get; init; } = ModLoaderKind.Unknown;
    public ModComponentKind Component { get; init; } = ModComponentKind.Unknown;
    public IReadOnlyList<string> OwnedFiles { get; init; } = Array.Empty<string>();
    public string? LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
}
