namespace HTFManager.Core.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public string? GamePath { get; set; }
    public string ActiveProfile { get; set; } = "Default";
    public string LastPage { get; set; } = "Home";
    public int WindowWidth { get; set; } = 1360;
    public int WindowHeight { get; set; } = 820;

    public bool AutoEnableNewMods { get; set; } = true;
    public bool KeepPackageCache { get; set; } = true;
    public bool PreserveConfigOnUninstall { get; set; } = true;
    public bool HideDeprecatedPackages { get; set; } = true;
    public bool ShowPackageInspector { get; set; } = true;
    public bool KeepLoaderPackageCache { get; set; } = true;
    public bool BackupConfigurationBeforeSave { get; set; } = true;
    public int MaxConfigurationBackups { get; set; } = 10;
    public bool DeveloperMode { get; set; } = false;
    public int AcknowledgedConfigSafetyVersion { get; set; } = 0;
}
