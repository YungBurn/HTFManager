namespace HTFManager.Core.Models;

public sealed class MelonLoaderEnvironmentInfo
{
    public bool Detected { get; init; }
    public bool Installed { get; init; }
    public bool Healthy { get; init; }
    public string Version { get; init; } = "—";
    public string? RootDirectory { get; init; }
    public string? ModsDirectory { get; init; }
    public string? PluginsDirectory { get; init; }
    public string? UserDataDirectory { get; init; }
    public string? LoaderConfigPath { get; init; }
    public string? LogsDirectory { get; init; }
    public string? ProxyPath { get; init; }
    public string? DobbyPath { get; init; }
}
