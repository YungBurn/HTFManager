namespace HTFManager.Core.Models;

public sealed class ProfileHealthItem
{
    public required ProfileModExpectation Expectation { get; init; }
    public InstalledMod? InstalledMod { get; init; }
    public required ProfileHealthStatus Status { get; init; }
    public required ProfileHealthMatchKind MatchKind { get; init; }
    public ProfileHealthReason Reason { get; init; }

    public string ExpectedVersion => Expectation.Requirement.Version;
    public string? InstalledVersion => InstalledMod?.Version;
}
