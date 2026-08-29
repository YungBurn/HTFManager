namespace HTFManager.Core.Models;

public sealed class ApplicationUpdateInfo
{
    public ApplicationUpdateState State { get; init; } = ApplicationUpdateState.Idle;
    public string CurrentVersion { get; init; } = "0.0.0";
    public string? LatestVersion { get; init; }
    public string? ReleaseName { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? ReleasePageUrl { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public ApplicationUpdateManifest? Manifest { get; init; }
    public string? AssetDownloadUrl { get; init; }
    public string? StagedPath { get; init; }
    public string? Error { get; init; }

    public bool IsUpdateAvailable => State is ApplicationUpdateState.Available or ApplicationUpdateState.Downloading or ApplicationUpdateState.Ready;
}
