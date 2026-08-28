namespace HTFManager.Core.Models;

public sealed class LoaderRecommendation
{
    public ModLoaderKind Loader { get; init; }
    public string Version { get; init; } = "—";
    public string SourceName { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
}
