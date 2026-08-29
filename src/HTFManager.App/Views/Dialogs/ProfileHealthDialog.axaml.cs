using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class ProfileHealthDialog : Window
{
    private string _profileName = "";

    public ProfileHealthDialog()
    {
        InitializeComponent();
        Opened += (_, _) => Render();
        App.Services.Localization.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => App.Services.Localization.LanguageChanged -= OnLanguageChanged;
    }

    public ProfileHealthDialog(ModProfile profile) : this()
    {
        _profileName = profile.Name;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Render();

    private ModProfile? CurrentProfile()
        => App.Services.Profiles.FirstOrDefault(profile =>
            profile.Name.Equals(_profileName, StringComparison.OrdinalIgnoreCase));

    private void Render()
    {
        var profile = CurrentProfile();
        if (profile is null)
        {
            Close();
            return;
        }

        var report = App.Services.GetProfileHealth(profile);
        ProfileNameText.Text = profile.Name;
        HealthyCountText.Text = report.HealthyCount.ToString();
        MissingCountText.Text = report.MissingCount.ToString();
        VersionCountText.Text = report.VersionMismatchCount.ToString();
        UncertainCountText.Text = report.IdentityUncertainCount.ToString();
        RestoreButton.IsVisible = report.MissingCount > 0;
        RestoreButton.IsEnabled = report.MissingCount > 0 && !App.Services.IsBusy;
        FooterText.Text = report.MissingCount > 0
            ? App.Services.Localization.Get("Health.MissingFooter")
            : report.VersionMismatchCount > 0 || report.IdentityUncertainCount > 0
                ? App.Services.Localization.Get("Health.WarningFooter")
                : App.Services.Localization.Get("Health.HealthyFooter");

        ItemsList.Children.Clear();
        foreach (var item in report.Items
                     .OrderBy(item => SortOrder(item.Status))
                     .ThenBy(item => item.Expectation.Requirement.Name, StringComparer.CurrentCultureIgnoreCase))
            ItemsList.Children.Add(CreateItem(item));
    }

    private Control CreateItem(ProfileHealthItem item)
    {
        var requirement = item.Expectation.Requirement;
        var brush = ResourceBrush(item.Status switch
        {
            ProfileHealthStatus.Healthy => "Brush.Success",
            ProfileHealthStatus.Missing => "Brush.Error",
            ProfileHealthStatus.VersionMismatch => "Brush.Warning",
            _ => "Brush.TextSecondary"
        });

        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(new TextBlock
        {
            Text = requirement.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var expected = NormalizeVersion(requirement.Version);
        var installed = item.InstalledMod is null ? "—" : NormalizeVersion(item.InstalledMod.Version);
        labels.Children.Add(new TextBlock
        {
            Text = string.Format(App.Services.Localization.Get("Health.VersionLine"), expected, installed),
            Foreground = brush,
            FontSize = 10
        });
        labels.Children.Add(new TextBlock
        {
            Text = BuildIdentityLine(item),
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });

        if (item.Status == ProfileHealthStatus.VersionMismatch)
        {
            labels.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Health.NoVersionRepair"),
                Foreground = ResourceBrush("Brush.Warning"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var badge = new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 4),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = StatusLabel(item.Status),
                Foreground = brush,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(labels);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        return new Border
        {
            Background = ResourceBrush("Brush.Surface"),
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10),
            Child = grid
        };
    }

    private string BuildIdentityLine(ProfileHealthItem item)
    {
        var requirement = item.Expectation.Requirement;
        var identity = !string.IsNullOrWhiteSpace(requirement.PackageKey)
            ? requirement.PackageKey
            : !string.IsNullOrWhiteSpace(requirement.IntrinsicId)
                ? requirement.IntrinsicId
                : requirement.FileName;
        var match = App.Services.Localization.Get(item.MatchKind switch
        {
            ProfileHealthMatchKind.PackageKey => "Health.Match.PackageKey",
            ProfileHealthMatchKind.IntrinsicId => "Health.Match.IntrinsicId",
            ProfileHealthMatchKind.ResolvedId => "Health.Match.ResolvedId",
            ProfileHealthMatchKind.LocalIdentity => "Health.Match.LocalIdentity",
            ProfileHealthMatchKind.Ambiguous => "Health.Match.Ambiguous",
            _ => "Health.Match.None"
        });
        return $"{identity} · {match}";
    }

    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        var profile = CurrentProfile();
        if (profile is null) return;
        await ProfileRestoreDialog.ShowAsync(this, profile);
        Render();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private string StatusLabel(ProfileHealthStatus status)
        => App.Services.Localization.Get(status switch
        {
            ProfileHealthStatus.Healthy => "Health.Healthy",
            ProfileHealthStatus.Missing => "Health.Missing",
            ProfileHealthStatus.VersionMismatch => "Health.VersionMismatch",
            _ => "Health.Uncertain"
        });

    private static int SortOrder(ProfileHealthStatus status) => status switch
    {
        ProfileHealthStatus.Missing => 0,
        ProfileHealthStatus.VersionMismatch => 1,
        ProfileHealthStatus.IdentityUncertain => 2,
        _ => 3
    };

    private static string NormalizeVersion(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;
}
