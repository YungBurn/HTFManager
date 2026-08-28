namespace HTFManager.Core.Models;

public sealed class BepInExEnvironmentInfo
{
    public bool Installed { get; init; }
    public bool Healthy { get; init; }
    public string Version { get; init; } = "—";
    public string? RootDirectory { get; init; }
    public string? PluginsDirectory { get; init; }
    public string? ConfigDirectory { get; init; }
    public string? PatchersDirectory { get; init; }
    public string? LogPath { get; init; }
    public string? BootstrapPath { get; init; }
}
