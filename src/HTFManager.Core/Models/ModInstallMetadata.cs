namespace HTFManager.Core.Models;

public sealed class ModInstallMetadata
{
    public ModSourceType Source { get; init; } = ModSourceType.LocalArchive;
    public string? PackageKey { get; init; }
    public string? IntrinsicId { get; init; }
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Dependencies { get; init; }
}
