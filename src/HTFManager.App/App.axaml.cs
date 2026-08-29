using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HTFManager.App.Services;
using HTFManager.App.Views;
using HTFManager.Infrastructure.Game;
using HTFManager.Infrastructure.Mods;
using HTFManager.Infrastructure.Loaders;
using HTFManager.Infrastructure.Profiles;
using HTFManager.Infrastructure.Storage;
using HTFManager.Infrastructure.System;
using HTFManager.Infrastructure.Thunderstore;
using HTFManager.Infrastructure.Configuration;
using HTFManager.Infrastructure.Updates;

namespace HTFManager.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        var localization = new LocalizationService(settingsStore, settings);
        localization.Initialize();

        var registry = new ModRegistryStore(settingsStore.DataDirectory);
        var loaderRegistry = new LoaderRegistryStore(settingsStore.DataDirectory);
        var modService = new ModService(registry);
        var artifactStore = new PackageArtifactStore(registry, settingsStore.DataDirectory);
        var packageService = new ModPackageService(registry, settingsStore.DataDirectory, artifactStore);
        var catalogService = new ThunderstoreCatalogService(settingsStore.DataDirectory);
        var loaderSetupService = new LoaderSetupService(loaderRegistry, settingsStore.DataDirectory);
        var configurationService = new ConfigurationService(settingsStore.DataDirectory);
        var profileService = new ProfileService(settingsStore);
        var profileHealthService = new ProfileHealthService();
        var profileBundleService = new ProfileBundleService(profileService, profileHealthService, artifactStore, settingsStore.DataDirectory);
        var reconciliationService = new ProfileVersionReconciliationService(profileHealthService, artifactStore);
        var updateService = new GitHubReleaseUpdateService(settingsStore.DataDirectory);
        var updateApplier = new WindowsApplicationUpdateApplier();

        Services = new AppServices(
            settingsStore,
            settings,
            localization,
            new SteamGameLocator(),
            new GameEnvironmentService(),
            modService,
            packageService,
            catalogService,
            loaderSetupService,
            configurationService,
            profileService,
            new ProfileRestoreService(),
            profileHealthService,
            profileBundleService,
            reconciliationService,
            artifactStore,
            updateService,
            updateApplier,
            new SystemShell(),
            new GameLauncher());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
        UpdateHostMode.SignalStartupReady();
    }
}
