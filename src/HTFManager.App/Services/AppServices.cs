using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.App.Services;

public sealed class AppServices
{
    public ISettingsStore SettingsStore { get; }
    public AppSettings Settings { get; }
    public LocalizationService Localization { get; }
    public ConfigurationLocalizationService ConfigLocalization { get; }
    public IGameLocator GameLocator { get; }
    public IGameEnvironmentService EnvironmentService { get; }
    public IModService ModService { get; }
    public IModPackageService ModPackageService { get; }
    public IModCatalogService CatalogService { get; }
    public ILoaderSetupService LoaderSetupService { get; }
    public IConfigurationService ConfigurationService { get; }
    public IProfileService ProfileService { get; }
    public ISystemShell Shell { get; }
    public IGameLauncher Launcher { get; }

    public GameEnvironmentInfo Environment { get; private set; } = new();
    public IReadOnlyList<InstalledMod> Mods { get; private set; } = Array.Empty<InstalledMod>();
    public IReadOnlyList<ModProfile> Profiles { get; private set; } = Array.Empty<ModProfile>();
    public IReadOnlyList<RemoteModPackage> Catalog { get; private set; } = Array.Empty<RemoteModPackage>();
    public IReadOnlyList<ModConfigurationDocument> Configurations { get; private set; } = Array.Empty<ModConfigurationDocument>();
    public string? RequestedConfigurationId { get; private set; }
    public bool CatalogLoaded { get; private set; }
    public bool IsBusy { get; private set; }
    public string? OperationMessage { get; private set; }
    public bool OperationSucceeded { get; private set; } = true;

    public event EventHandler? StateChanged;
    public event EventHandler? ConfigurationRequested;

    public AppServices(
        ISettingsStore settingsStore,
        AppSettings settings,
        LocalizationService localization,
        IGameLocator gameLocator,
        IGameEnvironmentService environmentService,
        IModService modService,
        IModPackageService modPackageService,
        IModCatalogService catalogService,
        ILoaderSetupService loaderSetupService,
        IConfigurationService configurationService,
        IProfileService profileService,
        ISystemShell shell,
        IGameLauncher launcher)
    {
        SettingsStore = settingsStore;
        Settings = settings;
        Localization = localization;
        ConfigLocalization = new ConfigurationLocalizationService();
        GameLocator = gameLocator;
        EnvironmentService = environmentService;
        ModService = modService;
        ModPackageService = modPackageService;
        CatalogService = catalogService;
        LoaderSetupService = loaderSetupService;
        ConfigurationService = configurationService;
        ProfileService = profileService;
        Shell = shell;
        Launcher = launcher;

        Refresh();
    }

    public void Refresh()
    {
        var gamePath = GameLocator.LocateGameDirectory(Settings.GamePath);
        if (!string.Equals(gamePath, Settings.GamePath, StringComparison.OrdinalIgnoreCase))
        {
            Settings.GamePath = gamePath;
            SettingsStore.Save(Settings);
        }

        Environment = EnvironmentService.Inspect(gamePath);
        Mods = ModService.Scan(Environment);
        Configurations = ConfigurationService.Scan(Environment, Mods);
        Profiles = ProfileService.LoadProfiles();
        ApplyCatalogState();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }


    public ModConfigurationDocument? FindConfigurationForMod(InstalledMod mod)
        => Configurations.FirstOrDefault(document =>
            !document.IsLoaderConfiguration &&
            string.Equals(document.AssociatedModId, mod.Id, StringComparison.OrdinalIgnoreCase));

    public ModConfigurationDocument? FindLoaderConfiguration(ModLoaderKind loader)
        => Configurations.FirstOrDefault(document => document.IsLoaderConfiguration && document.Loader == loader);

    public void RequestConfiguration(ModConfigurationDocument document)
    {
        RequestedConfigurationId = document.Id;
        ConfigurationRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConfigurationForMod(InstalledMod mod)
    {
        var document = FindConfigurationForMod(mod);
        if (document is not null)
            RequestConfiguration(document);
    }

    public void RequestLoaderConfiguration(ModLoaderKind loader)
    {
        var document = FindLoaderConfiguration(loader);
        if (document is not null)
            RequestConfiguration(document);
    }

    public bool SaveConfiguration(ModConfigurationDocument document)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.ConfigGameRunning"));
            return false;
        }

        var result = ConfigurationService.Save(
            document,
            Settings.BackupConfigurationBeforeSave,
            Math.Max(1, Settings.MaxConfigurationBackups));
        SetOperation(result.Success, Localization.Get(result.Success ? "Ops.ConfigSaved" : "Ops.ConfigSaveFailed") +
            (result.Success || string.IsNullOrWhiteSpace(result.Message) ? "" : ": " + result.Message));
        if (result.Success)
        {
            RequestedConfigurationId = document.Id;
            Configurations = ConfigurationService.Scan(Environment, Mods);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return result.Success;
    }

    public bool RestoreLatestConfiguration(ModConfigurationDocument document)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.ConfigGameRunning"));
            return false;
        }

        var result = ConfigurationService.RestoreLatest(document, Math.Max(2, Settings.MaxConfigurationBackups));
        SetOperation(result.Success, Localization.Get(result.Success ? "Ops.ConfigRestored" : "Ops.ConfigRestoreFailed") +
            (result.Success || string.IsNullOrWhiteSpace(result.Message) ? "" : ": " + result.Message));
        if (result.Success)
        {
            RequestedConfigurationId = document.Id;
            Configurations = ConfigurationService.Scan(Environment, Mods);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        return result.Success;
    }

    public IReadOnlyList<ConfigurationBackupInfo> GetConfigurationBackups(ModConfigurationDocument document)
        => ConfigurationService.GetBackups(document);

    public void OpenConfigurationFile(ModConfigurationDocument document)
    {
        if (File.Exists(document.FilePath)) Shell.OpenFile(document.FilePath);
    }

    public void OpenConfigurationFolder(ModConfigurationDocument document)
    {
        var directory = Path.GetDirectoryName(document.FilePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) Shell.OpenPath(directory);
    }

    public async Task LoadCatalogAsync(bool forceRefresh = false)
    {
        if (IsBusy) return;
        SetBusy(true, Localization.Get("Ops.LoadingCatalog"));
        try
        {
            Catalog = await CatalogService.GetPackagesAsync(forceRefresh);
            CatalogLoaded = true;
            ApplyCatalogState();
            SetOperation(true, Localization.Get("Ops.CatalogReady"));
        }
        catch (Exception ex)
        {
            SetOperation(false, Localization.Get("Ops.CatalogFailed") + ": " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    public bool SetGamePath(string path)
    {
        var normalized = GameLocator.LocateGameDirectory(path);
        if (normalized is null) return false;

        Settings.GamePath = normalized;
        SettingsStore.Save(Settings);
        Refresh();
        return true;
    }

    public bool ToggleMod(InstalledMod mod)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return false;
        }

        var changed = ModService.SetEnabled(mod, !mod.Enabled);
        SetOperation(changed, changed
            ? Localization.Get(mod.Enabled ? "Ops.ModDisabled" : "Ops.ModEnabled")
            : Localization.Get("Ops.ToggleFailed"));
        Refresh();
        return changed;
    }

    public async Task<PreparedModPackage?> PrepareLocalPackageAsync(string path)
    {
        if (!Environment.GameFound)
        {
            SetOperation(false, Localization.Get("Ops.EnvironmentNotReady"));
            return null;
        }
        if (!File.Exists(path)) return null;
        SetBusy(true, Localization.Get("Ops.InspectingPackage"));
        try
        {
            var inspection = await ModPackageService.InspectForInstallAsync(path, Environment);
            return new PreparedModPackage
            {
                SourcePath = path,
                Inspection = inspection
            };
        }
        finally { SetBusy(false); }
    }

    public async Task<PreparedModPackage?> PrepareRemotePackageAsync(RemoteModPackage package)
    {
        if (!Environment.GameFound)
        {
            SetOperation(false, Localization.Get("Ops.EnvironmentNotReady"));
            return null;
        }
        var version = package.LatestVersion;
        if (version is null) return null;
        SetBusy(true, Localization.Get("Ops.InspectingPackage"));
        try
        {
            var archive = await CatalogService.DownloadPackageAsync(package, version);
            var metadata = new ModInstallMetadata
            {
                Source = ModSourceType.Thunderstore,
                PackageKey = package.FullName,
                Name = package.Name,
                Version = version.VersionNumber,
                Author = package.Owner,
                Description = version.Description,
                Dependencies = version.Dependencies
            };
            var inspection = await ModPackageService.InspectForInstallAsync(archive, Environment, metadata);
            return new PreparedModPackage
            {
                SourcePath = archive,
                Metadata = metadata,
                Inspection = inspection,
                RemotePackage = package
            };
        }
        catch (Exception ex)
        {
            SetOperation(false, Localization.Get("Ops.InstallFailed") + ": " + ex.Message);
            return null;
        }
        finally { SetBusy(false); }
    }

    public async Task<bool> InstallPreparedPackageAsync(PreparedModPackage prepared)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return false;
        }
        if (!prepared.Inspection.IsValid || prepared.Inspection.RiskLevel == PackageRiskLevel.Blocked)
        {
            SetOperation(false, prepared.Inspection.Error ?? Localization.Get("Ops.InstallFailed"));
            return false;
        }
        if (prepared.Inspection.MissingLoader)
        {
            SetOperation(false, prepared.Inspection.Loader == ModLoaderKind.BepInEx
                ? Localization.Get("Ops.BepInExNotReady")
                : Localization.Get("Ops.MelonLoaderNotReady"));
            return false;
        }

        SetBusy(true, Localization.Get("Ops.Installing"));
        try
        {
            if (prepared.Inspection.Loader == ModLoaderKind.BepInEx && prepared.Inspection.Dependencies.Count > 0)
            {
                if (!await EnsureDependenciesAsync(prepared.Inspection.Dependencies, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                    return false;
            }

            var result = await ModPackageService.InstallAsync(
                prepared.SourcePath, Environment, prepared.Metadata, Settings.AutoEnableNewMods, Settings.KeepPackageCache);
            SetOperation(result.Success, Localization.Get(result.Success ? "Ops.InstallSuccess" : "Ops.InstallFailed") +
                (result.Success ? $" — {prepared.Inspection.Name}" : ": " + result.Message));
            Refresh();
            return result.Success;
        }
        finally { SetBusy(false); }
    }

    public bool IsDependencySatisfied(string dependency)
    {
        if (!TryParseDependency(dependency, out var packageKey, out var requiredVersion)) return false;
        if (IsBepInExDependency(packageKey)) return Environment.BepInEx.Healthy;
        var installed = Mods.FirstOrDefault(m => string.Equals(m.PackageKey, packageKey, StringComparison.OrdinalIgnoreCase));
        return installed is not null && CompareVersions(installed.Version, requiredVersion) >= 0;
    }

    public LoaderRecommendation GetLoaderRecommendation(ModLoaderKind loader) => LoaderSetupService.GetRecommendation(loader);
    public LoaderInstallRecord? GetManagedLoaderRecord(ModLoaderKind loader) => LoaderSetupService.GetManagedRecord(loader, Environment.GameDirectory);
    public IReadOnlyList<DiagnosticItem> ValidateLoader(ModLoaderKind loader) => LoaderSetupService.Validate(loader, Environment);

    public async Task<bool> InstallOrRepairLoaderAsync(ModLoaderKind loader)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return false;
        }
        SetBusy(true, Localization.Get("Ops.LoaderInstalling"));
        try
        {
            var result = await LoaderSetupService.InstallOrRepairAsync(loader, Environment, Settings.KeepLoaderPackageCache);
            SetOperation(result.Success, result.Success ? Localization.Get("Ops.LoaderInstallSuccess") : Localization.Get("Ops.LoaderInstallFailed") + ": " + result.Message);
            Refresh();
            return result.Success;
        }
        finally { SetBusy(false); }
    }

    public bool UninstallLoader(ModLoaderKind loader)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return false;
        }
        var result = LoaderSetupService.Uninstall(loader, Environment);
        SetOperation(result.Success, result.Success ? Localization.Get("Ops.LoaderUninstallSuccess") : Localization.Get("Ops.LoaderUninstallFailed") + ": " + result.Message);
        Refresh();
        return result.Success;
    }

    public void OpenLoaderSource(ModLoaderKind loader)
    {
        var recommendation = LoaderSetupService.GetRecommendation(loader);
        if (!string.IsNullOrWhiteSpace(recommendation.SourceUrl)) Shell.OpenUri(recommendation.SourceUrl);
    }

    public async Task InstallLocalFilesAsync(IEnumerable<string> paths)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return;
        }

        if (!Environment.GameFound)
        {
            SetOperation(false, Localization.Get("Ops.EnvironmentNotReady"));
            return;
        }

        var supported = paths
            .Where(File.Exists)
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (supported.Length == 0)
        {
            SetOperation(false, Localization.Get("Ops.UnsupportedFile"));
            return;
        }

        SetBusy(true, Localization.Get("Ops.InspectingPackage"));
        try
        {
            foreach (var path in supported)
            {
                var inspection = await ModPackageService.InspectAsync(path);
                if (!inspection.IsValid)
                {
                    SetOperation(false, Localization.Get("Ops.InstallFailed") + ": " + (inspection.Error ?? Path.GetFileName(path)));
                    continue;
                }

                if (inspection.Loader == ModLoaderKind.BepInEx && !Environment.BepInEx.Healthy)
                {
                    SetOperation(false, Localization.Get("Ops.BepInExNotReady"));
                    continue;
                }

                if (inspection.Loader == ModLoaderKind.MelonLoader && !Environment.MelonLoader.Healthy)
                {
                    SetOperation(false, Localization.Get("Ops.MelonLoaderNotReady"));
                    continue;
                }

                if (inspection.Loader == ModLoaderKind.BepInEx && inspection.Dependencies.Count > 0)
                {
                    var dependenciesOk = await EnsureDependenciesAsync(inspection.Dependencies, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    if (!dependenciesOk)
                        continue;
                }

                var result = await ModPackageService.InstallAsync(
                    path,
                    Environment,
                    metadata: null,
                    autoEnable: Settings.AutoEnableNewMods,
                    keepPackageCache: Settings.KeepPackageCache);

                SetOperation(result.Success,
                    Localization.Get(result.Success ? "Ops.InstallSuccess" : "Ops.InstallFailed") +
                    (result.Success ? $" — {inspection.Name}" : ": " + result.Message));
                Refresh();
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task InstallRemotePackageAsync(RemoteModPackage package)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return;
        }

        if (!Environment.GameFound || !Environment.BepInExFound)
        {
            SetOperation(false, Localization.Get("Ops.EnvironmentNotReady"));
            return;
        }

        SetBusy(true, Localization.Get("Ops.Installing"));
        try
        {
            var ok = await EnsureRemotePackageAsync(package, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (ok)
                SetOperation(true, Localization.Get("Ops.InstallSuccess") + $" — {package.Name}");
        }
        catch (Exception ex)
        {
            SetOperation(false, Localization.Get("Ops.InstallFailed") + ": " + ex.Message);
        }
        finally
        {
            Refresh();
            SetBusy(false);
        }
    }

    public async Task UpdateModAsync(InstalledMod mod)
    {
        if (string.IsNullOrWhiteSpace(mod.PackageKey))
        {
            SetOperation(false, Localization.Get("Ops.NoUpdateSource"));
            return;
        }

        if (!CatalogLoaded)
            await LoadCatalogAsync();

        var package = Catalog.FirstOrDefault(p => p.FullName.Equals(mod.PackageKey, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            SetOperation(false, Localization.Get("Ops.NoUpdateSource"));
            return;
        }

        await InstallRemotePackageAsync(package);
    }

    public bool UninstallMod(InstalledMod mod)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return false;
        }

        var result = ModPackageService.Uninstall(mod, Environment, Settings.PreserveConfigOnUninstall);
        SetOperation(result.Success,
            Localization.Get(result.Success ? "Ops.UninstallSuccess" : "Ops.UninstallFailed") +
            (result.Success ? $" — {mod.Name}" : ": " + result.Message));
        Refresh();
        return result.Success;
    }

    public RemoteModPackage? FindCatalogPackage(InstalledMod mod)
        => string.IsNullOrWhiteSpace(mod.PackageKey)
            ? null
            : Catalog.FirstOrDefault(p => p.FullName.Equals(mod.PackageKey, StringComparison.OrdinalIgnoreCase));

    public void OpenPackagePage(RemoteModPackage package)
    {
        if (!string.IsNullOrWhiteSpace(package.PackageUrl)) Shell.OpenUri(package.PackageUrl);
    }

    public void OpenModFolder(InstalledMod mod)
    {
        var directory = Directory.Exists(mod.FilePath) ? mod.FilePath : Path.GetDirectoryName(mod.FilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Shell.OpenPath(directory);
    }

    public void LaunchGame()
    {
        Launcher.Launch(Environment);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenGameDirectory()
    {
        if (Environment.GameDirectory is not null) Shell.OpenPath(Environment.GameDirectory);
    }

    public void OpenPluginsDirectory()
    {
        if (Environment.BepInExFound && Environment.PluginsDirectory is not null && Directory.Exists(Environment.PluginsDirectory))
            Shell.OpenPath(Environment.PluginsDirectory);
    }

    public void OpenMelonModsDirectory()
    {
        var path = Environment.MelonLoader.ModsDirectory;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Shell.OpenPath(path);
    }

    public void OpenMelonPluginsDirectory()
    {
        var path = Environment.MelonLoader.PluginsDirectory;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Shell.OpenPath(path);
    }

    public void OpenMelonLogsDirectory()
    {
        var path = Environment.MelonLoader.LogsDirectory;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Shell.OpenPath(path);
    }

    public void OpenConfigDirectory()
    {
        if (Environment.BepInExFound && Environment.ConfigDirectory is not null && Directory.Exists(Environment.ConfigDirectory))
            Shell.OpenPath(Environment.ConfigDirectory);
    }

    public void OpenLog()
    {
        if (Environment.LogPath is not null) Shell.OpenFile(Environment.LogPath);
    }

    public void ReportOperation(bool success, string message) => SetOperation(success, message);

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }


    public ProfilePackageInspection InspectProfilePackage(string packagePath)
        => ProfileService.InspectPortablePackage(packagePath, Mods);

    public bool ExportProfile(ModProfile profile, string destinationPath)
    {
        var result = ProfileService.ExportPortablePackage(profile, Mods, destinationPath);
        SetOperation(result.Success, Localization.Get(result.Success ? "Ops.ProfileExported" : "Ops.ProfileExportFailed") +
            (result.Success || string.IsNullOrWhiteSpace(result.Message) ? "" : ": " + result.Message));
        return result.Success;
    }

    public bool ImportProfile(string packagePath, ProfilePackageInspection inspection, string? importName = null)
    {
        var targetName = string.IsNullOrWhiteSpace(importName) ? inspection.ImportName : importName.Trim();
        var result = ProfileService.ImportPortablePackage(packagePath, Mods, targetName);
        SetOperation(result.Success, Localization.Get(result.Success ? "Ops.ProfileImported" : "Ops.ProfileImportFailed") +
            (result.Success ? $" · {targetName}" : string.IsNullOrWhiteSpace(result.Message) ? "" : ": " + result.Message));
        Refresh();
        return result.Success;
    }

    public void ResolveMissingProfileMods(ModProfile profile)
    {
        var before = profile.UnresolvedMods.Count;
        var result = ProfileService.ResolveMissingMods(profile, Mods);
        var resolved = Math.Max(0, before - profile.UnresolvedMods.Count);
        SetOperation(result.Success, result.Success
            ? string.Format(Localization.Get("Ops.ProfileResolvedMissing"), resolved)
            : Localization.Get("Ops.ProfileResolveFailed") + ": " + result.Message);
        Refresh();
    }

    public void RemoveMissingProfileMod(ModProfile profile, string portableId)
    {
        ProfileService.RemoveMissingMod(profile, portableId);
        SetOperation(true, Localization.Get("Ops.ProfileMissingRemoved"));
        Refresh();
    }

    public void SaveCurrentProfile(string name, bool includeConfigurationSnapshot = true)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (includeConfigurationSnapshot && Configurations.Any(document => document.DirtyCount > 0))
        {
            SetOperation(false, Localization.Get("Ops.ProfileUnsavedConfig"));
            return;
        }

        var profile = ProfileService.Capture(name.Trim(), Mods);

        if (includeConfigurationSnapshot)
        {
            var snapshot = ProfileService.CaptureConfigurationSnapshots(profile, Configurations, Environment.GameDirectory);
            if (!snapshot.Success)
            {
                SetOperation(false, Localization.Get("Ops.ProfileSnapshotFailed") + ": " + snapshot.Message);
                Refresh();
                return;
            }
        }
        else
        {
            ProfileService.ClearConfigurationSnapshots(profile);
        }

        Settings.ActiveProfile = profile.Name;
        SettingsStore.Save(Settings);
        SetOperation(true, Localization.Get("Ops.ProfileSaved"));
        Refresh();
    }

    public void ApplyProfile(ModProfile profile)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return;
        }
        if (Configurations.Any(document => document.DirtyCount > 0))
        {
            SetOperation(false, Localization.Get("Ops.ProfileUnsavedConfig"));
            return;
        }
        if (profile.UnresolvedMods.Count > 0)
        {
            SetOperation(false, string.Format(Localization.Get("Ops.ProfileHasMissingMods"), profile.UnresolvedMods.Count));
            return;
        }

        var result = ProfileService.Apply(profile, Mods, ModService, Environment.GameDirectory);
        if (!result.Success)
        {
            SetOperation(false, Localization.Get("Ops.ProfileApplyFailed") + ": " + result.Message);
            Refresh();
            return;
        }

        Settings.ActiveProfile = profile.Name;
        SettingsStore.Save(Settings);
        SetOperation(true, Localization.Get("Ops.ProfileApplied") +
            (result.ConfigurationCount > 0 ? $" · {result.ConfigurationCount} {Localization.Get("Profiles.ConfigFiles")}" : ""));
        Refresh();
    }

    public void UpdateProfileConfigurationSnapshot(ModProfile profile)
    {
        if (Launcher.IsRunning(Environment))
        {
            SetOperation(false, Localization.Get("Ops.GameRunningBlocked"));
            return;
        }
        if (Configurations.Any(document => document.DirtyCount > 0))
        {
            SetOperation(false, Localization.Get("Ops.ProfileUnsavedConfig"));
            return;
        }

        var result = ProfileService.CaptureConfigurationSnapshots(profile, Configurations, Environment.GameDirectory);
        SetOperation(result.Success, Localization.Get(result.Success ? "Ops.ProfileSnapshotUpdated" : "Ops.ProfileSnapshotFailed") +
            (result.Success ? $" · {result.ConfigurationCount} {Localization.Get("Profiles.ConfigFiles")}" : ": " + result.Message));
        Refresh();
    }

    public void ClearProfileConfigurationSnapshot(ModProfile profile)
    {
        var result = ProfileService.ClearConfigurationSnapshots(profile);
        SetOperation(result.Success, Localization.Get(result.Success ? "Ops.ProfileSnapshotCleared" : "Ops.ProfileSnapshotFailed") +
            (result.Success ? "" : ": " + result.Message));
        Refresh();
    }

    public void AddModToProfile(ModProfile profile, InstalledMod mod)
    {
        profile.ModStates[mod.Id] = mod.Enabled;
        ProfileService.Save(profile);
        Refresh();
    }

    public void RemoveModFromProfile(ModProfile profile, string modId)
    {
        if (!profile.ModStates.Remove(modId)) return;
        ProfileService.Save(profile);
        Refresh();
    }

    public void SetProfileModState(ModProfile profile, string modId, bool enabled)
    {
        if (!profile.ModStates.ContainsKey(modId)) return;
        profile.ModStates[modId] = enabled;
        ProfileService.Save(profile);
        Refresh();
    }

    public void DeleteProfile(ModProfile profile)
    {
        ProfileService.Delete(profile);
        var remaining = ProfileService.LoadProfiles();
        if (profile.Name.Equals(Settings.ActiveProfile, StringComparison.OrdinalIgnoreCase))
            Settings.ActiveProfile = remaining.FirstOrDefault()?.Name ?? "Default";
        SettingsStore.Save(Settings);
        Refresh();
    }

    private async Task<bool> EnsureRemotePackageAsync(RemoteModPackage package, HashSet<string> visiting)
    {
        if (!visiting.Add(package.FullName))
            return true;

        var version = package.LatestVersion;
        if (version is null)
        {
            SetOperation(false, Localization.Get("Ops.InstallFailed") + ": no active version");
            return false;
        }

        if (!await EnsureDependenciesAsync(version.Dependencies, visiting))
            return false;

        var existing = Mods.FirstOrDefault(m => string.Equals(m.PackageKey, package.FullName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && CompareVersions(existing.Version, version.VersionNumber) >= 0)
            return true;

        var archive = await CatalogService.DownloadPackageAsync(package, version);
        var metadata = new ModInstallMetadata
        {
            Source = ModSourceType.Thunderstore,
            PackageKey = package.FullName,
            Name = package.Name,
            Version = version.VersionNumber,
            Author = package.Owner,
            Description = version.Description,
            Dependencies = version.Dependencies
        };

        var result = await ModPackageService.InstallAsync(
            archive,
            Environment,
            metadata,
            autoEnable: Settings.AutoEnableNewMods,
            keepPackageCache: Settings.KeepPackageCache);

        if (!result.Success)
        {
            SetOperation(false, Localization.Get("Ops.InstallFailed") + ": " + result.Message);
            return false;
        }

        Refresh();
        return true;
    }

    private async Task<bool> EnsureDependenciesAsync(IEnumerable<string> dependencies, HashSet<string> visiting)
    {
        var dependencyList = dependencies.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (dependencyList.Length == 0) return true;

        if (!CatalogLoaded)
        {
            try
            {
                Catalog = await CatalogService.GetPackagesAsync();
                CatalogLoaded = true;
                ApplyCatalogState();
            }
            catch (Exception ex)
            {
                SetOperation(false, Localization.Get("Ops.DependencyFailed") + ": " + ex.Message);
                return false;
            }
        }

        foreach (var dependency in dependencyList)
        {
            if (!TryParseDependency(dependency, out var packageKey, out var requiredVersion))
                continue;

            if (IsBepInExDependency(packageKey) && Environment.BepInExFound)
                continue;

            var installed = Mods.FirstOrDefault(m => string.Equals(m.PackageKey, packageKey, StringComparison.OrdinalIgnoreCase));
            if (installed is not null && CompareVersions(installed.Version, requiredVersion) >= 0)
                continue;

            var remote = Catalog.FirstOrDefault(p => p.FullName.Equals(packageKey, StringComparison.OrdinalIgnoreCase));
            if (remote is null)
            {
                SetOperation(false, Localization.Get("Ops.DependencyFailed") + $": {packageKey}");
                return false;
            }

            if (!await EnsureRemotePackageAsync(remote, visiting))
                return false;
        }

        return true;
    }

    private void ApplyCatalogState()
    {
        if (Catalog.Count == 0) return;
        foreach (var mod in Mods)
        {
            if (string.IsNullOrWhiteSpace(mod.PackageKey)) continue;
            var package = Catalog.FirstOrDefault(p => p.FullName.Equals(mod.PackageKey, StringComparison.OrdinalIgnoreCase));
            var latest = package?.LatestVersion;
            if (latest is null) continue;
            mod.LatestVersion = latest.VersionNumber;
            mod.UpdateAvailable = CompareVersions(mod.Version, latest.VersionNumber) < 0;
        }
    }

    private static bool TryParseDependency(string value, out string packageKey, out string version)
    {
        packageKey = "";
        version = "0.0.0";
        var lastDash = value.LastIndexOf('-');
        if (lastDash <= 0 || lastDash == value.Length - 1) return false;
        packageKey = value[..lastDash];
        version = value[(lastDash + 1)..];
        return packageKey.Contains('-');
    }

    private static bool IsBepInExDependency(string packageKey)
        => packageKey.Contains("BepInEx", StringComparison.OrdinalIgnoreCase) ||
           packageKey.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase);

    private static int CompareVersions(string left, string right)
    {
        static Version Parse(string value)
        {
            var cleaned = value.Trim().TrimStart('v', 'V');
            if (Version.TryParse(cleaned, out var parsed)) return parsed;
            var numeric = new string(cleaned.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');
            return Version.TryParse(numeric, out parsed) ? parsed : new Version(0, 0, 0);
        }

        return Parse(left).CompareTo(Parse(right));
    }

    private void SetBusy(bool busy, string? message = null)
    {
        IsBusy = busy;
        if (busy) OperationSucceeded = true;
        if (message is not null) OperationMessage = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetOperation(bool success, string message)
    {
        OperationSucceeded = success;
        OperationMessage = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
