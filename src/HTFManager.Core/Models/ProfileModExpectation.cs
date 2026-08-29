namespace HTFManager.Core.Models;

public sealed class ProfileModExpectation
{
    public ProfileModRequirement Requirement { get; set; } = new();
    public string? ResolvedModId { get; set; }
    public ProfileExpectationMetadataQuality MetadataQuality { get; set; } = ProfileExpectationMetadataQuality.Complete;
}
