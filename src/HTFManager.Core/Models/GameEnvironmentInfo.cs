namespace HTFManager.Core.Models;

public sealed class GameEnvironmentInfo
{
    public string? GameDirectory { get; init; }
    public string? ExecutablePath { get; init; }
    public bool GameFound { get; init; }
    public string GameVersion { get; init; } = "—";

    public BepInExEnvironmentInfo BepInEx { get; init; } = new();
    public MelonLoaderEnvironmentInfo MelonLoader { get; init; } = new();

    // Compatibility aliases retained for the v0.2 application layer.
    public bool BepInExFound => BepInEx.Installed;
    public string BepInExVersion => BepInEx.Version;
    public string? PluginsDirectory => BepInEx.PluginsDirectory;
    public string? ConfigDirectory => BepInEx.ConfigDirectory;
    public string? LogPath => BepInEx.LogPath;

    public bool IsHealthy => GameFound && (BepInEx.Healthy || MelonLoader.Healthy);
}
