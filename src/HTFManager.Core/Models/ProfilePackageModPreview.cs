namespace HTFManager.Core.Models;

public sealed class ProfilePackageModPreview
{
    public required ProfileModRequirement Requirement { get; init; }
    public bool Matched { get; init; }
    public string? MatchedInstalledModId { get; init; }
    public string? MatchedInstalledModName { get; init; }
    public string? MatchedInstalledVersion { get; init; }
    public bool VersionMatches { get; init; } = true;
}
