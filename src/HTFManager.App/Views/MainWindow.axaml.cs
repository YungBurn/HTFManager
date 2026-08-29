using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HTFManager.App.Views.Discover;
using HTFManager.App.Views.Configuration;
using HTFManager.App.Views.Home;
using HTFManager.App.Views.Mods;
using HTFManager.App.Views.Profiles;
using HTFManager.App.Views.Settings;
using HTFManager.App.Views.Tools;
using HTFManager.App.Views.Dialogs;

namespace HTFManager.App.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Control> _pages;
    private readonly Dictionary<string, Button> _buttons;

    public MainWindow()
    {
        InitializeComponent();

        _pages = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = new HomeView(),
            ["Mods"] = new ModsView(),
            ["Discover"] = new DiscoverView(),
            ["Profiles"] = new ProfilesView(),
            ["Configuration"] = new ConfigurationView(),
            ["Tools"] = new ToolsView(),
            ["Settings"] = new SettingsView()
        };

        _buttons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = HomeButton,
            ["Mods"] = ModsButton,
            ["Discover"] = DiscoverButton,
            ["Profiles"] = ProfilesButton,
            ["Configuration"] = ConfigurationButton,
            ["Tools"] = ToolsButton,
            ["Settings"] = SettingsButton
        };

        Width = Math.Max(MinWidth, App.Services.Settings.WindowWidth);
        Height = Math.Max(MinHeight, App.Services.Settings.WindowHeight);
        App.Services.StateChanged += OnStateChanged;
        App.Services.Localization.LanguageChanged += OnLanguageChanged;
        App.Services.ConfigurationRequested += (_, _) => NavigateTo("Configuration");
        Closing += HandleClosing;

        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragEnterHandler(this, OnDragEnter);
        DragDrop.AddDragLeaveHandler(this, OnDragLeave);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);

        NavigateTo("Home");
        UpdateStatus();
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
            NavigateTo(page);
    }

    private void NavigateTo(string? page)
    {
        if (page is null || !_pages.TryGetValue(page, out var content))
            page = "Home";

        content = _pages[page];
        PageHost.Content = content;
        if (content is DiscoverView discover)
            _ = discover.ActivateAsync();

        foreach (var pair in _buttons)
        {
            pair.Value.Classes.Remove("active");
            if (pair.Key.Equals(page, StringComparison.OrdinalIgnoreCase))
                pair.Value.Classes.Add("active");
        }

    }

    private void OnStateChanged(object? sender, EventArgs e) => UpdateStatus();
    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        if (!string.IsNullOrWhiteSpace(App.Services.OperationMessage))
        {
            StatusText.Text = App.Services.OperationMessage;
            StatusDot.Fill = App.Services.IsBusy
                ? (IBrush?)this.FindResource("Brush.Primary")
                : App.Services.OperationSucceeded
                    ? (IBrush?)this.FindResource("Brush.Success")
                    : (IBrush?)this.FindResource("Brush.Error");
            return;
        }

        var healthy = App.Services.Environment.IsHealthy;
        StatusText.Text = healthy
            ? App.Services.Localization.Get("Status.Ready")
            : App.Services.Localization.Get("Status.GameMissing");
        StatusDot.Fill = healthy
            ? (IBrush?)this.FindResource("Brush.Success")
            : (IBrush?)this.FindResource("Brush.Warning");
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var accepted = CanAcceptDrop(e);
        DropOverlay.IsVisible = accepted;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepted = CanAcceptDrop(e);
        DropOverlay.IsVisible = accepted;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (!CanAcceptDrop(e)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        var paths = files.Select(f => f.Path.LocalPath).Where(File.Exists).ToArray();
        if (paths.Length == 0) return;

        var bundlePackages = paths.Where(path => path.EndsWith(".htfbundle", StringComparison.OrdinalIgnoreCase)).ToArray();
        var profilePackages = paths.Where(path => path.EndsWith(".htfprofile", StringComparison.OrdinalIgnoreCase)).ToArray();
        var modPackages = paths.Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                                              path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (bundlePackages.Length > 0 || profilePackages.Length > 0)
            NavigateTo("Profiles");

        foreach (var path in bundlePackages)
        {
            var inspection = App.Services.InspectProfileBundle(path);
            if (!inspection.IsValid)
            {
                App.Services.ReportOperation(false, App.Services.Localization.Get("Ops.ProfileBundleImportFailed") + ": " + inspection.Error);
                continue;
            }
            await new ProfileBundleImportDialog(path, inspection).ShowDialog<bool>(this);
        }

        foreach (var path in profilePackages)
        {
            var inspection = App.Services.InspectProfilePackage(path);
            if (!inspection.IsValid)
            {
                App.Services.ReportOperation(false, App.Services.Localization.Get("Ops.ProfileImportFailed") + ": " + inspection.Error);
                continue;
            }
            await new ProfileImportDialog(path, inspection).ShowDialog<bool>(this);
        }

        if (modPackages.Length == 0) return;
        NavigateTo("Mods");
        if (!App.Services.Settings.ShowPackageInspector)
        {
            await App.Services.InstallLocalFilesAsync(modPackages);
            return;
        }
        foreach (var path in modPackages)
            await PackageInspectorDialog.ShowForLocalAsync(this, path);
    }

    private static bool CanAcceptDrop(DragEventArgs e)
    {
        if (App.Services.IsBusy || !e.DataTransfer.Formats.Contains(DataFormat.File))
            return false;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return false;
        return files.Any(f =>
        {
            var path = f.Path.LocalPath;
            return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".htfprofile", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".htfbundle", StringComparison.OrdinalIgnoreCase);
        });
    }

    private void HandleClosing(object? sender, WindowClosingEventArgs e)
    {
        App.Services.Settings.WindowWidth = (int)Math.Round(Width);
        App.Services.Settings.WindowHeight = (int)Math.Round(Height);
        App.Services.SettingsStore.Save(App.Services.Settings);
    }
}
