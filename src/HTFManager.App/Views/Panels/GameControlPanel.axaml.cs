using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Panels;

public partial class GameControlPanel : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _updatingProfiles;

    public GameControlPanel()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Refresh();
        App.Services.Localization.LanguageChanged += (_, _) => Refresh();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshRunningState();
        _timer.Start();

        Refresh();
    }

    private void Refresh()
    {
        var env = App.Services.Environment;
        var ready = env.GameFound;
        GameStateText.Text = ready ? App.Services.Localization.Get("Common.Ready") : App.Services.Localization.Get("Common.NotFound");
        GameVersionText.Text = $"{env.GameVersion}";
        GameDot.Fill = ready ? ResourceBrush("Brush.Success") : ResourceBrush("Brush.Warning");

        EnvGameText.Text = ready ? App.Services.Localization.Get("Common.Ready") : App.Services.Localization.Get("Common.NotFound");
        EnvBepText.Text = env.BepInEx.Installed ? env.BepInEx.Version : App.Services.Localization.Get("Common.NotFound");
        EnvMelonText.Text = env.MelonLoader.Installed
            ? env.MelonLoader.Version
            : env.MelonLoader.Detected
                ? App.Services.Localization.Get("Common.NeedsAttention")
                : App.Services.Localization.Get("Common.NotFound");
        EnvModsText.Text = App.Services.Mods.Count.ToString();

        OpenBepModsButton.IsVisible = env.BepInEx.Installed;
        OpenConfigButton.IsVisible = env.BepInEx.Installed;
        ViewBepLogButton.IsVisible = env.BepInEx.Installed;
        OpenMelonModsButton.IsVisible = env.MelonLoader.Detected && Directory.Exists(env.MelonLoader.ModsDirectory);
        OpenMelonPluginsButton.IsVisible = env.MelonLoader.Detected && Directory.Exists(env.MelonLoader.PluginsDirectory);
        ViewMelonLogsButton.IsVisible = env.MelonLoader.Detected && Directory.Exists(env.MelonLoader.LogsDirectory);
        ActivityText.Text = string.IsNullOrWhiteSpace(App.Services.OperationMessage)
            ? App.Services.Localization.Get("Right.NoActivity")
            : App.Services.OperationMessage;
        ActivityText.Foreground = App.Services.OperationSucceeded
            ? ResourceBrush(App.Services.IsBusy ? "Brush.Primary" : "Brush.TextSecondary")
            : ResourceBrush("Brush.Error");
        PlayButton.IsEnabled = ready && !App.Services.Launcher.IsRunning(env) && !App.Services.IsBusy;

        _updatingProfiles = true;
        var names = App.Services.Profiles.Select(p => p.Name).ToList();
        if (!names.Contains(App.Services.Settings.ActiveProfile, StringComparer.OrdinalIgnoreCase))
            names.Insert(0, App.Services.Settings.ActiveProfile);
        ProfileCombo.ItemsSource = names;
        ProfileCombo.SelectedItem = names.FirstOrDefault(n => n.Equals(App.Services.Settings.ActiveProfile, StringComparison.OrdinalIgnoreCase));
        _updatingProfiles = false;

        RefreshRunningState();
    }

    private void RefreshRunningState()
    {
        var running = App.Services.Launcher.IsRunning(App.Services.Environment);
        PlayButton.IsEnabled = App.Services.Environment.GameFound && !running && !App.Services.IsBusy;
        PlayText.Text = running
            ? App.Services.Localization.Get("Right.Running")
            : App.Services.Localization.Get("Right.Play");
    }

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        try { App.Services.LaunchGame(); }
        catch { }
        Refresh();
    }

    private void BrowseGame_Click(object? sender, RoutedEventArgs e) => App.Services.OpenGameDirectory();
    private void OpenMods_Click(object? sender, RoutedEventArgs e) => App.Services.OpenPluginsDirectory();
    private void OpenConfig_Click(object? sender, RoutedEventArgs e) => App.Services.OpenConfigDirectory();
    private void OpenMelonMods_Click(object? sender, RoutedEventArgs e) => App.Services.OpenMelonModsDirectory();
    private void OpenMelonPlugins_Click(object? sender, RoutedEventArgs e) => App.Services.OpenMelonPluginsDirectory();
    private void ViewLog_Click(object? sender, RoutedEventArgs e) => App.Services.OpenLog();
    private void ViewMelonLogs_Click(object? sender, RoutedEventArgs e) => App.Services.OpenMelonLogsDirectory();
    private void Refresh_Click(object? sender, RoutedEventArgs e) => App.Services.Refresh();

    private void ProfileCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfiles || ProfileCombo.SelectedItem is not string selected)
            return;

        if (App.Services.Launcher.IsRunning(App.Services.Environment))
        {
            Refresh();
            return;
        }

        var profile = App.Services.Profiles.FirstOrDefault(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (profile is not null && !profile.Name.Equals(App.Services.Settings.ActiveProfile, StringComparison.OrdinalIgnoreCase))
            App.Services.ApplyProfile(profile);
    }
}
