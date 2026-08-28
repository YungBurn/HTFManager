using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class ProfileImportDialog : Window
{
    private readonly string _packagePath;
    private readonly ProfilePackageInspection _inspection;

    public ProfileImportDialog(string packagePath, ProfilePackageInspection inspection)
    {
        _packagePath = packagePath;
        _inspection = inspection;
        InitializeComponent();
        Opened += (_, _) => Render();
    }

    private void Render()
    {
        ImportNameBox.Text = _inspection.ImportName;
        FormatText.Text = $"HTF Profile · schema {_inspection.SchemaVersion}";
        VersionText.Text = string.IsNullOrWhiteSpace(_inspection.ExportedWithVersion) ? "—" : _inspection.ExportedWithVersion;
        ModsText.Text = _inspection.Mods.Count.ToString();
        ConfigsText.Text = _inspection.ConfigurationCount == 0
            ? App.Services.Localization.Get("ProfileImport.None")
            : $"{_inspection.ConfigurationCount} · {FormatSize(_inspection.ConfigurationBytes)}";
        ExportedAtText.Text = _inspection.ExportedUtc?.ToLocalTime().ToString("g") ?? "—";
        MatchSummaryText.Text = string.Format(
            App.Services.Localization.Get("ProfileImport.MatchSummary"),
            _inspection.MatchedCount,
            _inspection.MissingCount,
            _inspection.VersionMismatchCount);

        MissingPanel.IsVisible = _inspection.MissingCount > 0;
        MissingText.Text = string.Format(App.Services.Localization.Get("ProfileImport.MissingText"), _inspection.MissingCount);
        StatusText.Text = _inspection.MissingCount > 0
            ? App.Services.Localization.Get("ProfileImport.ImportWithMissing")
            : _inspection.VersionMismatchCount > 0
                ? App.Services.Localization.Get("ProfileImport.ImportWithVersionMismatch")
                : App.Services.Localization.Get("ProfileImport.Ready");

        ModList.Children.Clear();
        foreach (var preview in _inspection.Mods.OrderBy(item => item.Requirement.Name, StringComparer.CurrentCultureIgnoreCase))
            ModList.Children.Add(CreateModRow(preview));
        ImportButton.IsEnabled = _inspection.IsValid && !string.IsNullOrWhiteSpace(ImportNameBox.Text) && !App.Services.IsBusy;
    }

    private Control CreateModRow(ProfilePackageModPreview preview)
    {
        var requirement = preview.Requirement;
        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(new TextBlock
        {
            Text = requirement.Name,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        labels.Children.Add(new TextBlock
        {
            Text = $"{LoaderLabel(requirement.Loader)} · {requirement.Version}" +
                   (string.IsNullOrWhiteSpace(requirement.PackageKey) ? "" : $" · {requirement.PackageKey}") +
                   (preview.Matched && !preview.VersionMatches
                       ? $" · {App.Services.Localization.Get("ProfileImport.InstalledVersion")}: {preview.MatchedInstalledVersion ?? "—"}"
                       : ""),
            Foreground = this.FindResource("Brush.TextSecondary") as IBrush,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var statusKey = !preview.Matched
            ? "ProfileImport.Missing"
            : preview.VersionMatches
                ? "ProfileImport.Matched"
                : "ProfileImport.VersionMismatch";
        var statusBrush = preview.Matched && preview.VersionMatches ? "Brush.Success" : "Brush.Warning";
        var status = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 3),
            BorderThickness = new Thickness(1),
            BorderBrush = this.FindResource(statusBrush) as IBrush,
            Child = new TextBlock
            {
                Text = App.Services.Localization.Get(statusKey),
                FontSize = 9,
                Foreground = this.FindResource(statusBrush) as IBrush
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(labels);
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);
        return new Border
        {
            Background = this.FindResource("Brush.Elevated") as IBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7),
            Child = grid
        };
    }

    private void ImportNameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ImportButton is not null)
            ImportButton.IsEnabled = _inspection.IsValid && !string.IsNullOrWhiteSpace(ImportNameBox.Text) && !App.Services.IsBusy;
    }

    private void Import_Click(object? sender, RoutedEventArgs e)
    {
        var name = ImportNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        ImportButton.IsEnabled = false;
        var success = App.Services.ImportProfile(_packagePath, _inspection, name);
        if (success) Close(true);
        else ImportButton.IsEnabled = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private static string LoaderLabel(ModLoaderKind loader) => loader switch
    {
        ModLoaderKind.BepInEx => "BepInEx",
        ModLoaderKind.MelonLoader => "MelonLoader",
        _ => "Unknown"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:0.##} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0.##} KB";
        return $"{bytes} B";
    }
}
