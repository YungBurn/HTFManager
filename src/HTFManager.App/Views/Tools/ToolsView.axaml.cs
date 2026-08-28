using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HTFManager.Core.Models;
using HTFManager.App.Views.Dialogs;

namespace HTFManager.App.Views.Tools;

public partial class ToolsView : UserControl
{
    private bool _isAttached;

    public ToolsView()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Refresh();
        App.Services.Localization.LanguageChanged += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { _isAttached = true; Refresh(); };
        DetachedFromVisualTree += (_, _) => _isAttached = false;
    }

    private void Refresh()
    {
        if (!_isAttached) return;
        var env = App.Services.Environment;
        GameDirectoryText.Text = env.GameDirectory ?? App.Services.Localization.Get("Common.NotFound");
        BepStatusText.Text = env.BepInEx.Healthy ? App.Services.Localization.Get("Common.Ready") : App.Services.Localization.Get("Common.NotFound");
        BepVersionText.Text = env.BepInEx.Version;
        BepRecommendedText.Text = App.Services.GetLoaderRecommendation(ModLoaderKind.BepInEx).Version;
        MelonStatusText.Text = env.MelonLoader.Healthy
            ? App.Services.Localization.Get("Common.Ready")
            : env.MelonLoader.Detected
                ? App.Services.Localization.Get("Common.NeedsAttention")
                : App.Services.Localization.Get("Common.NotFound");
        MelonVersionText.Text = env.MelonLoader.Version;
        MelonRecommendedText.Text = App.Services.GetLoaderRecommendation(ModLoaderKind.MelonLoader).Version;
        ManageBepButtonText.Text = App.Services.Localization.Get(env.BepInEx.Healthy ? "Tools.ManageLoader" : "Tools.AutoInstall");
        ManageMelonButtonText.Text = App.Services.Localization.Get(env.MelonLoader.Healthy ? "Tools.ManageLoader" : "Tools.AutoInstall");
        OpenBepButton.IsEnabled = env.BepInEx.RootDirectory is not null && Directory.Exists(env.BepInEx.RootDirectory);
        ConfigureBepButton.IsEnabled = App.Services.FindLoaderConfiguration(ModLoaderKind.BepInEx) is not null;
        OpenMelonButton.IsEnabled = env.MelonLoader.RootDirectory is not null && Directory.Exists(env.MelonLoader.RootDirectory);
        OpenMelonLogsButton.IsEnabled = env.MelonLoader.LogsDirectory is not null && Directory.Exists(env.MelonLoader.LogsDirectory);
        ConfigureMelonButton.IsEnabled = App.Services.FindLoaderConfiguration(ModLoaderKind.MelonLoader) is not null;
        RenderDiagnostics(App.Services.EnvironmentService.Diagnose(env));
    }

    private async void SelectFolder_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = App.Services.Localization.Get("Tools.SelectFolder"),
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is null) return;
        App.Services.SetGamePath(folder.Path.LocalPath);
    }

    private void AutoDetect_Click(object? sender, RoutedEventArgs e)
    {
        App.Services.Settings.GamePath = null;
        App.Services.SettingsStore.Save(App.Services.Settings);
        App.Services.Refresh();
    }

    private void Diagnostics_Click(object? sender, RoutedEventArgs e) => Refresh();

    private void OpenBep_Click(object? sender, RoutedEventArgs e)
    {
        var path = App.Services.Environment.BepInEx.RootDirectory;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) App.Services.Shell.OpenPath(path);
    }

    private void OpenMelon_Click(object? sender, RoutedEventArgs e)
    {
        var path = App.Services.Environment.MelonLoader.RootDirectory;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) App.Services.Shell.OpenPath(path);
    }

    private void OpenMelonLogs_Click(object? sender, RoutedEventArgs e) => App.Services.OpenMelonLogsDirectory();

    private async void ManageBep_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await new LoaderSetupDialog(ModLoaderKind.BepInEx).ShowDialog<bool>(owner);
    }

    private async void ManageMelon_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await new LoaderSetupDialog(ModLoaderKind.MelonLoader).ShowDialog<bool>(owner);
    }


    private void ConfigureBep_Click(object? sender, RoutedEventArgs e)
        => App.Services.RequestLoaderConfiguration(ModLoaderKind.BepInEx);

    private void ConfigureMelon_Click(object? sender, RoutedEventArgs e)
        => App.Services.RequestLoaderConfiguration(ModLoaderKind.MelonLoader);

    private void BepSource_Click(object? sender, RoutedEventArgs e) => App.Services.OpenLoaderSource(ModLoaderKind.BepInEx);
    private void MelonSource_Click(object? sender, RoutedEventArgs e) => App.Services.OpenLoaderSource(ModLoaderKind.MelonLoader);

    private void RenderDiagnostics(IReadOnlyList<DiagnosticItem> items)
    {
        DiagnosticsList.Children.Clear();
        foreach (var item in items)
        {
            var icon = new PathIcon
            {
                Data = this.FindResource(item.Passed ? "Icon.Check" : "Icon.Warning") as Geometry,
                Width = 14,
                Height = 14,
                Foreground = ResourceBrush(item.Passed ? "Brush.Success" : "Brush.Warning"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var name = new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center };
            var result = new TextBlock
            {
                Text = App.Services.Localization.Get(item.Passed ? "Tools.Passed" : "Tools.Failed"),
                Foreground = ResourceBrush(item.Passed ? "Brush.Success" : "Brush.Warning"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(icon);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            Grid.SetColumn(result, 2);
            row.Children.Add(result);
            DiagnosticsList.Children.Add(row);
        }
    }

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;
}
