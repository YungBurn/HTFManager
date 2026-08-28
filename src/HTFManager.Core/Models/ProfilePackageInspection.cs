namespace HTFManager.Core.Models;

public sealed class ProfilePackageInspection
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public string ProfileName { get; init; } = "Imported Profile";
    public string ImportName { get; init; } = "Imported Profile";
    public int SchemaVersion { get; init; }
    public string ExportedWithVersion { get; init; } = "";
    public DateTimeOffset? ExportedUtc { get; init; }
    public IReadOnlyList<ProfilePackageModPreview> Mods { get; init; } = Array.Empty<ProfilePackageModPreview>();
    public int ConfigurationCount { get; init; }
    public long ConfigurationBytes { get; init; }
    public int MatchedCount => Mods.Count(item => item.Matched);
    public int MissingCount => Mods.Count(item => !item.Matched);
    public int VersionMismatchCount => Mods.Count(item => item.Matched && !item.VersionMatches);
    public bool IncludesConfigurationSnapshots => ConfigurationCount > 0;

    public static ProfilePackageInspection Invalid(string error)
        => new() { IsValid = false, Error = error };
}
