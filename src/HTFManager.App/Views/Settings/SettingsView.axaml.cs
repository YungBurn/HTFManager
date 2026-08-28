using Avalonia.Controls;
using Avalonia.Interactivity;

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
        var backupOptions = new[] { 5, 10, 20, 50 };
        var backupIndex = Array.IndexOf(backupOptions, App.Services.Settings.MaxConfigurationBackups);
        MaxBackupsCombo.SelectedIndex = backupIndex >= 0 ? backupIndex : 1;
        _loading = false;
    }

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

    private void MaxBackupsCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || MaxBackupsCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string value || !int.TryParse(value, out var count))
            return;
        App.Services.Settings.MaxConfigurationBackups = count;
        App.Services.SaveSettings();
    }

    private void OpenData_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Services.SettingsStore.DataDirectory);
        App.Services.Shell.OpenPath(App.Services.SettingsStore.DataDirectory);
    }
}
