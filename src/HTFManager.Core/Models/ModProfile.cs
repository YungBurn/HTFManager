namespace HTFManager.Core.Models;

public sealed class ModProfile
{
    public string Name { get; set; } = "Default";
    public Dictionary<string, bool> ModStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProfileConfigurationSnapshot> ConfigurationSnapshots { get; set; } = new();
    public List<ProfileModRequirement> UnresolvedMods { get; set; } = new();
    public DateTime? ConfigurationSnapshotCapturedUtc { get; set; }
}
