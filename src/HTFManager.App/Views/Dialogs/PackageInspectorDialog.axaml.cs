using Avalonia.Controls;
using Avalonia.Interactivity;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class PackageInspectorDialog : Window
{
    private PreparedModPackage _prepared;

    public PackageInspectorDialog(PreparedModPackage prepared)
    {
        _prepared = prepared;
        InitializeComponent();
        Closed += (_, _) => App.Services.CleanupPreparedPackage(_prepared);
        Render();
    }

    public static async Task<bool> ShowForLocalAsync(Window owner, string path)
    {
        var prepared = await App.Services.PrepareLocalPackageAsync(path);
        if (prepared is null) return false;
        var dialog = new PackageInspectorDialog(prepared);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<bool> ShowForBundledAsync(
        Window owner,
        string bundlePath,
        HtfBundlePayloadDescriptor descriptor,
        ProfileModRequirement requirement)
    {
        var prepared = await App.Services.PrepareBundledPackageAsync(bundlePath, descriptor, requirement);
        if (prepared is null) return false;
        var dialog = new PackageInspectorDialog(prepared);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<bool> ShowForRemoteAsync(Window owner, RemoteModPackage package)
    {
        var prepared = await App.Services.PrepareRemotePackageAsync(package);
        if (prepared is null) return false;
        var dialog = new PackageInspectorDialog(prepared);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<bool> ShowForRemoteAsync(
        Window owner,
        RemoteModPackage package,
        RemoteModVersion version)
    {
        var prepared = await App.Services.PrepareRemotePackageAsync(package, version);
        if (prepared is null) return false;
        var dialog = new PackageInspectorDialog(prepared);
        return await dialog.ShowDialog<bool>(owner);
    }

    private void Render()
    {
        var i = _prepared.Inspection;
        PackageNameText.Text = i.Name;
        PackageAuthorText.Text = i.Author;
        DescriptionText.Text = i.Description;
        DescriptionText.IsVisible = !string.IsNullOrWhiteSpace(i.Description);
        VersionText.Text = i.Version;
        LoaderText.Text = LoaderLabel(i.Loader);
        ComponentText.Text = ComponentLabel(i.Component);
        SourceText.Text = SourceLabel(i.Source);
        TargetText.Text = i.TargetSummary;
        SizeText.Text = FormatSize(i.PackageSize);
        FileCountText.Text = $"{i.TargetFiles.Count} {App.Services.Localization.Get("Inspector.FileCount")}";
        RiskText.Text = App.Services.Localization.Get(i.RiskLevel switch
        {
            PackageRiskLevel.Safe => "Inspector.Risk.Safe",
            PackageRiskLevel.Warning => "Inspector.Risk.Warning",
            _ => "Inspector.Risk.Blocked"
        });

        ModeText.Text = i.IsUpgrade
            ? $"{App.Services.Localization.Get("Inspector.UpdateMode")}: {i.ExistingVersion ?? "—"} → {i.Version}"
            : App.Services.Localization.Get("Inspector.InstallMode");
        InstallButtonText.Text = App.Services.Localization.Get(i.IsUpgrade ? "Common.Update" : "Common.Install");

        MissingLoaderPanel.IsVisible = i.MissingLoader;
        MissingLoaderText.Text = string.Format(App.Services.Localization.Get("Inspector.LoaderMissingText"), LoaderLabel(i.Loader));
        InstallLoaderButton.IsEnabled = i.Loader is ModLoaderKind.BepInEx or ModLoaderKind.MelonLoader && !App.Services.IsBusy;

        FilesList.Children.Clear();
        foreach (var file in i.TargetFiles)
            FilesList.Children.Add(new TextBlock { Text = file, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (i.TargetFiles.Count == 0)
            FilesList.Children.Add(new TextBlock { Text = App.Services.Localization.Get("Inspector.NoFiles"), Classes = { "secondary" } });

        DependenciesList.Children.Clear();
        foreach (var dependency in i.Dependencies)
        {
            var satisfied = App.Services.IsDependencySatisfied(dependency);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = satisfied ? "✓" : "!" });
            var text = new TextBlock { Text = dependency, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            DependenciesList.Children.Add(row);
        }
        if (i.Dependencies.Count == 0)
            DependenciesList.Children.Add(new TextBlock { Text = App.Services.Localization.Get("Inspector.NoDependencies"), Classes = { "secondary" } });

        IssuesList.Children.Clear();
        var issues = new List<string>();
        if (!string.IsNullOrWhiteSpace(i.Error)) issues.Add(i.Error);
        issues.AddRange(i.Conflicts.Select(x => App.Services.Localization.Get("Inspector.ConflictPrefix") + ": " + x));
        issues.AddRange(i.Warnings);
        IssuesPanel.IsVisible = issues.Count > 0;
        foreach (var issue in issues.Distinct())
            IssuesList.Children.Add(new TextBlock { Text = "• " + issue, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 });

        InstallButton.IsEnabled = i.IsValid && i.RiskLevel != PackageRiskLevel.Blocked && !i.MissingLoader && !App.Services.IsBusy;
    }

    private async void InstallLoader_Click(object? sender, RoutedEventArgs e)
    {
        var loader = _prepared.Inspection.Loader;
        if (loader is not (ModLoaderKind.BepInEx or ModLoaderKind.MelonLoader)) return;
        var setup = new LoaderSetupDialog(loader);
        await setup.ShowDialog<bool>(this);
        var refreshed = await App.Services.ModPackageService.InspectForInstallAsync(
            _prepared.SourcePath, App.Services.Environment, _prepared.Metadata);
        _prepared = new PreparedModPackage
        {
            SourcePath = _prepared.SourcePath,
            Metadata = _prepared.Metadata,
            RemotePackage = _prepared.RemotePackage,
            TemporaryDirectory = _prepared.TemporaryDirectory,
            Inspection = refreshed
        };
        Render();
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        var success = await App.Services.InstallPreparedPackageAsync(_prepared);
        if (success) Close(true);
        else
        {
            var refreshed = await App.Services.ModPackageService.InspectForInstallAsync(
                _prepared.SourcePath, App.Services.Environment, _prepared.Metadata);
            _prepared = new PreparedModPackage
            {
                SourcePath = _prepared.SourcePath,
                Metadata = _prepared.Metadata,
                RemotePackage = _prepared.RemotePackage,
                TemporaryDirectory = _prepared.TemporaryDirectory,
                Inspection = refreshed
            };
            Render();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private static string LoaderLabel(ModLoaderKind loader) => loader switch
    {
        ModLoaderKind.BepInEx => "BepInEx",
        ModLoaderKind.MelonLoader => "MelonLoader",
        _ => App.Services.Localization.Get("Mods.Component.Unknown")
    };

    private static string ComponentLabel(ModComponentKind component) => App.Services.Localization.Get(component switch
    {
        ModComponentKind.Plugin => "Mods.Component.Plugin",
        ModComponentKind.Mod => "Mods.Component.Mod",
        ModComponentKind.Patcher => "Mods.Component.Patcher",
        ModComponentKind.Content => "Mods.Component.Content",
        _ => "Mods.Component.Unknown"
    });

    private static string SourceLabel(ModSourceType source) => App.Services.Localization.Get(source switch
    {
        ModSourceType.Thunderstore => "Mods.Source.Thunderstore",
        ModSourceType.LocalDll => "Mods.Source.LocalDll",
        ModSourceType.Development => "Mods.Source.Development",
        ModSourceType.External => "Mods.Source.External",
        _ => "Mods.Source.LocalArchive"
    });

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:0.##} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0.##} KB";
        return $"{bytes} B";
    }
}
