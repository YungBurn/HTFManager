namespace HTFManager.Core.Models;

public sealed class LoaderInstallRecord
{
    public ModLoaderKind Loader { get; set; }
    public string GameDirectory { get; set; } = "";
    public string Version { get; set; } = "—";
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string> Files { get; set; } = new();
}
