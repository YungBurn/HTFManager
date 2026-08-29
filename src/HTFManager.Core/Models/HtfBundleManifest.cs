namespace HTFManager.Core.Models;

public sealed class HtfBundleManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string GeneratedWithVersion { get; set; } = "0.3.7";
    public string ProfileEntry { get; set; } = "profile.htfprofile";
    public string ProfileSha256 { get; set; } = "";
    public List<HtfBundlePayloadDescriptor> Payloads { get; set; } = new();
}
