namespace HTFManager.Core.Models;

public sealed class ModConfigurationDocument
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string FilePath { get; init; }
    public ModLoaderKind Loader { get; init; } = ModLoaderKind.Unknown;
    public bool IsLoaderConfiguration { get; init; }
    public string? PluginGuid { get; init; }
    public string? AssociatedModId { get; set; }
    public string? DetectedVersion { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public List<ConfigurationEntry> Entries { get; init; } = new();

    public int DirtyCount => Entries.Count(entry => entry.IsDirty);
    public IReadOnlyList<string> Sections => Entries
        .Select(entry => entry.Section)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
}
