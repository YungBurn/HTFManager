using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class ProfileBundleImportDialog : Window
{
    private string _bundlePath = "";
    private ProfileBundleInspection _inspection = ProfileBundleInspection.Invalid("", "No bundle loaded.");

    public ProfileBundleImportDialog()
    {
        InitializeComponent();
        Opened += (_, _) => Render(setImportName: true);
        App.Services.Localization.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => App.Services.Localization.LanguageChanged -= OnLanguageChanged;
    }

    public ProfileBundleImportDialog(string bundlePath, ProfileBundleInspection inspection) : this()
    {
        _bundlePath = bundlePath;
        _inspection = inspection;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Render(setImportName: false);

    private void Render(bool setImportName)
    {
        var profile = _inspection.ProfileInspection;
        if (!_inspection.IsValid || profile is null)
        {
            ImportButton.IsEnabled = false;
            return;
        }

        if (setImportName)
            ImportNameBox.Text = profile.ImportName;
        HealthyCountText.Text = _inspection.HealthyCount.ToString();
        BundledCountText.Text = _inspection.BundledMissingCount.ToString();
        RemoteCountText.Text = _inspection.UnbundledMissingCount.ToString();
        VersionCountText.Text = _inspection.VersionMismatchCount.ToString();
        UncertainCountText.Text = _inspection.IdentityUncertainCount.ToString();

        ItemsList.Children.Clear();
        foreach (var item in _inspection.Items
                     .OrderBy(item => SortOrder(item.Health.Status))
                     .ThenBy(item => item.Health.Expectation.Requirement.Name, StringComparer.CurrentCultureIgnoreCase))
            ItemsList.Children.Add(CreateItem(item));

        ImportButton.IsEnabled = !string.IsNullOrWhiteSpace(ImportNameBox.Text) && !App.Services.IsBusy;
    }

    private Control CreateItem(ProfileBundleInspectionItem item)
    {
        var health = item.Health;
        var requirement = health.Expectation.Requirement;
        var status = health.Status == ProfileHealthStatus.Missing && item.BundledPayload is not null
            ? App.Services.Localization.Get("BundleImport.BundledExact")
            : App.Services.Localization.Get(health.Status switch
            {
                ProfileHealthStatus.Healthy => "Health.Healthy",
                ProfileHealthStatus.Missing => "Health.Missing",
                ProfileHealthStatus.VersionMismatch => "Health.VersionMismatch",
                _ => "Health.Uncertain"
            });
        var brush = ResourceBrush(health.Status switch
        {
            ProfileHealthStatus.Healthy => "Brush.Success",
            ProfileHealthStatus.Missing when item.BundledPayload is not null => "Brush.Primary",
            ProfileHealthStatus.Missing => "Brush.Error",
            ProfileHealthStatus.VersionMismatch => "Brush.Warning",
            _ => "Brush.TextSecondary"
        });

        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(new TextBlock { Text = requirement.Name, FontWeight = FontWeight.SemiBold, FontSize = 12 });
        labels.Children.Add(new TextBlock
        {
            Text = $"{requirement.Version} · {DisplayIdentity(requirement)}",
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var badge = new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 3),
            Child = new TextBlock { Text = status, Foreground = brush, FontSize = 9, FontWeight = FontWeight.SemiBold }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(labels);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        return new Border
        {
            Background = ResourceBrush("Brush.Surface"),
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
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

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var name = ImportNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        ImportButton.IsEnabled = false;

        if (!App.Services.ImportProfileBundle(_bundlePath, _inspection, name))
        {
            ImportButton.IsEnabled = true;
            return;
        }

        if (OpenRestoreCheck.IsChecked == true)
        {
            var profile = App.Services.Profiles.FirstOrDefault(profile =>
                profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (profile is not null && App.Services.GetProfileHealth(profile).MissingCount > 0)
                await ProfileRestoreDialog.ShowAsync(this, profile);
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private static int SortOrder(ProfileHealthStatus status) => status switch
    {
        ProfileHealthStatus.Missing => 0,
        ProfileHealthStatus.VersionMismatch => 1,
        ProfileHealthStatus.IdentityUncertain => 2,
        _ => 3
    };

    private static string DisplayIdentity(ProfileModRequirement requirement)
        => !string.IsNullOrWhiteSpace(requirement.PackageKey)
            ? requirement.PackageKey
            : !string.IsNullOrWhiteSpace(requirement.IntrinsicId)
                ? requirement.IntrinsicId
                : requirement.FileName;

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;
}
