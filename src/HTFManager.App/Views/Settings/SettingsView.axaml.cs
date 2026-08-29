using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Settings;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        _loading = true;
        LanguageCombo.SelectedIndex = App.Services.Settings.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        DataDirectoryText.Text = App.Services.SettingsStore.DataDirectory;
        AutoEnableCheck.IsChecked = App.Services.Settings.AutoEnableNewMods;
        KeepCacheCheck.IsChecked = App.Services.Settings.KeepPackageCache;
        PreserveConfigCheck.IsChecked = App.Services.Settings.PreserveConfigOnUninstall;
        HideDeprecatedCheck.IsChecked = App.Services.Settings.HideDeprecatedPackages;
        ShowInspectorCheck.IsChecked = App.Services.Settings.ShowPackageInspector;
        KeepLoaderCacheCheck.IsChecked = App.Services.Settings.KeepLoaderPackageCache;
        BackupConfigCheck.IsChecked = App.Services.Settings.BackupConfigurationBeforeSave;
        DeveloperModeCheck.IsChecked = App.Services.Settings.DeveloperMode;
        AutoUpdateCheck.IsChecked = App.Services.Settings.AutomaticallyCheckForUpdates;
        var backupOptions = new[] { 5, 10, 20, 50 };
        var backupIndex = Array.IndexOf(backupOptions, App.Services.Settings.MaxConfigurationBackups);
        MaxBackupsCombo.SelectedIndex = backupIndex >= 0 ? backupIndex : 1;
        _loading = false;

        App.Services.StateChanged += OnStateChanged;
        App.Services.Localization.LanguageChanged += OnLanguageChanged;
        RenderUpdateState();
    }

    private void OnStateChanged(object? sender, EventArgs e) => RenderUpdateState();
    private void OnLanguageChanged(object? sender, EventArgs e) => RenderUpdateState();

    private void LanguageCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
            return;

        App.Services.Localization.SetLanguage(language);
    }

    private void ModSetting_Click(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Services.Settings.AutoEnableNewMods = AutoEnableCheck.IsChecked == true;
        App.Services.Settings.KeepPackageCache = KeepCacheCheck.IsChecked == true;
        App.Services.Settings.PreserveConfigOnUninstall = PreserveConfigCheck.IsChecked == true;
        App.Services.Settings.HideDeprecatedPackages = HideDeprecatedCheck.IsChecked == true;
        App.Services.Settings.ShowPackageInspector = ShowInspectorCheck.IsChecked == true;
        App.Services.Settings.KeepLoaderPackageCache = KeepLoaderCacheCheck.IsChecked == true;
        App.Services.SaveSettings();
    }

    private void ConfigSetting_Click(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Services.Settings.BackupConfigurationBeforeSave = BackupConfigCheck.IsChecked == true;
        App.Services.SaveSettings();
    }

    private void DeveloperMode_Click(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Services.Settings.DeveloperMode = DeveloperModeCheck.IsChecked == true;
        App.Services.SaveSettings();
    }

    private void UpdateSetting_Click(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Services.Settings.AutomaticallyCheckForUpdates = AutoUpdateCheck.IsChecked == true;
        App.Services.SaveSettings();
    }

    private void MaxBackupsCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || MaxBackupsCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string value || !int.TryParse(value, out var count))
            return;
        App.Services.Settings.MaxConfigurationBackups = count;
        App.Services.SaveSettings();
    }

    private async void CheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        try { await App.Services.CheckForApplicationUpdatesAsync(force: true); }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    private async void DownloadUpdate_Click(object? sender, RoutedEventArgs e)
    {
        DownloadUpdateButton.IsEnabled = false;
        try { await App.Services.DownloadApplicationUpdateAsync(); }
        finally { DownloadUpdateButton.IsEnabled = true; }
    }

    private void RestartUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (!App.Services.StartApplicationUpdate(out var error))
        {
            App.Services.ReportOperation(false,
                App.Services.Localization.Get("Settings.UpdateApplyUnavailable") +
                (string.IsNullOrWhiteSpace(error) ? "" : ": " + error));
            RenderUpdateState();
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ViewRelease_Click(object? sender, RoutedEventArgs e)
        => App.Services.OpenApplicationReleasePage();

    private void RenderUpdateState()
    {
        if (CurrentVersionText is null) return;
        var update = App.Services.ApplicationUpdate;
        CurrentVersionText.Text = App.Services.ApplicationVersion;
        LatestVersionText.Text = string.IsNullOrWhiteSpace(update.LatestVersion) ? "—" : update.LatestVersion;
        LastCheckedText.Text = App.Services.Settings.LastUpdateCheckUtc is { } checkedAt
            ? checkedAt.ToLocalTime().ToString("g")
            : App.Services.Localization.Get("Settings.Never");

        UpdateStatusText.Text = update.State switch
        {
            ApplicationUpdateState.Checking => App.Services.Localization.Get("Settings.UpdateChecking"),
            ApplicationUpdateState.UpToDate => App.Services.Localization.Get("Settings.UpdateCurrent"),
            ApplicationUpdateState.Available => App.Services.Localization.Get("Settings.UpdateAvailable"),
            ApplicationUpdateState.Downloading => App.Services.Localization.Get("Settings.UpdateDownloading"),
            ApplicationUpdateState.Ready => BuildReadyStatus(update),
            ApplicationUpdateState.Error => App.Services.Localization.Get("Settings.UpdateError") +
                                            (string.IsNullOrWhiteSpace(update.Error) ? "" : ": " + update.Error),
            _ => App.Services.Localization.Get("Settings.UpdateIdle")
        };

        CheckUpdateButton.IsEnabled = update.State is not (ApplicationUpdateState.Checking or ApplicationUpdateState.Downloading);
        DownloadUpdateButton.IsVisible = update.State == ApplicationUpdateState.Available;
        RestartUpdateButton.IsVisible = update.State == ApplicationUpdateState.Ready && App.Services.CanApplyApplicationUpdate(out _);
        ViewReleaseButton.IsVisible = !string.IsNullOrWhiteSpace(update.ReleasePageUrl);
    }

    private string BuildReadyStatus(ApplicationUpdateInfo update)
    {
        var status = App.Services.Localization.Get("Settings.UpdateReady");
        if (!App.Services.CanApplyApplicationUpdate(out var reason) && !string.IsNullOrWhiteSpace(reason))
            status += " " + App.Services.Localization.Get("Settings.UpdateApplyUnavailable") + ": " + reason;
        return status;
    }

    private void OpenData_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Services.SettingsStore.DataDirectory);
        App.Services.Shell.OpenPath(App.Services.SettingsStore.DataDirectory);
    }
}
