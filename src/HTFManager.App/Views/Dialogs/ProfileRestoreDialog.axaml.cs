using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class ProfileRestoreDialog : Window
{
    private string _profileName = "";
    private ProfileRestorePlan? _plan;
    private bool _changed;
    private bool _refreshing;

    public ProfileRestoreDialog()
    {
        InitializeComponent();
        Opened += async (_, _) => await RefreshPlanAsync(resolveLocal: true, forceCatalogRefresh: false);
        App.Services.Localization.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => App.Services.Localization.LanguageChanged -= OnLanguageChanged;
    }

    public ProfileRestoreDialog(ModProfile profile) : this()
    {
        _profileName = profile.Name;
        ProfileNameText.Text = profile.Name;
    }

    public static Task<bool> ShowAsync(Window owner, ModProfile profile)
        => new ProfileRestoreDialog(profile).ShowDialog<bool>(owner);

    private async void OnLanguageChanged(object? sender, EventArgs e)
    {
        await RefreshPlanAsync(resolveLocal: false, forceCatalogRefresh: false);
    }

    private ModProfile? FindCurrentProfile()
        => App.Services.Profiles.FirstOrDefault(profile =>
            profile.Name.Equals(_profileName, StringComparison.OrdinalIgnoreCase));

    private async Task RefreshPlanAsync(bool resolveLocal, bool forceCatalogRefresh)
    {
        if (_refreshing) return;
        _refreshing = true;
        SetLocalBusy(true);

        try
        {
            var profile = FindCurrentProfile();
            if (profile is null)
            {
                Close(_changed);
                return;
            }

            ProfileNameText.Text = profile.Name;

            if (resolveLocal && profile.UnresolvedMods.Count > 0)
            {
                App.Services.ResolveMissingProfileMods(profile);
                profile = FindCurrentProfile();
                if (profile is null)
                {
                    Close(_changed);
                    return;
                }
            }

            if (profile.UnresolvedMods.Count == 0)
            {
                _plan = new ProfileRestorePlan { ProfileName = profile.Name };
                RenderPlan();
                return;
            }

            if (!App.Services.CatalogLoaded || forceCatalogRefresh)
                await App.Services.LoadCatalogAsync(forceCatalogRefresh);

            if (!App.Services.CatalogLoaded)
            {
                ShowCatalogError();
                return;
            }

            profile = FindCurrentProfile();
            if (profile is null)
            {
                Close(_changed);
                return;
            }

            _plan = App.Services.ProfileRestoreService.BuildPlan(profile, App.Services.Catalog);
            RenderPlan();
        }
        finally
        {
            _refreshing = false;
            SetLocalBusy(false);
        }
    }

    private void RenderPlan()
    {
        var plan = _plan ?? new ProfileRestorePlan { ProfileName = _profileName };

        ReadyCountText.Text = plan.ReadyCount.ToString();
        FallbackCountText.Text = plan.VersionFallbackCount.ToString();
        UnavailableCountText.Text = plan.PackageUnavailableCount.ToString();
        ManualCountText.Text = plan.ManualRequiredCount.ToString();

        LoadingPanel.IsVisible = false;
        CatalogErrorPanel.IsVisible = false;
        CompletePanel.IsVisible = plan.IsComplete;
        PlanPanel.IsVisible = !plan.IsComplete;
        ResolveAgainButton.IsEnabled = !App.Services.IsBusy;

        if (plan.IsComplete)
        {
            ItemsList.Children.Clear();
            FooterStatusText.Text = App.Services.Localization.Get("Restore.CompleteFooter");
            return;
        }

        ItemsList.Children.Clear();
        foreach (var item in plan.Items
                     .OrderBy(item => SortOrder(item.Disposition))
                     .ThenBy(item => item.Requirement.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ItemsList.Children.Add(CreateRestoreItem(item));
        }

        FooterStatusText.Text = !App.Services.Environment.GameFound
            ? App.Services.Localization.Get("Restore.EnvironmentRequired")
            : App.Services.Launcher.IsRunning(App.Services.Environment)
                ? App.Services.Localization.Get("Restore.GameRunning")
                : string.Format(
                    App.Services.Localization.Get("Restore.FooterSummary"),
                    plan.InstallableCount,
                    plan.TotalCount);
    }

    private Control CreateRestoreItem(ProfileRestoreItem item)
    {
        var requirement = item.Requirement;
        var statusBrush = ResourceBrush(item.Disposition switch
        {
            ProfileRestoreDisposition.Ready => "Brush.Success",
            ProfileRestoreDisposition.VersionFallback => "Brush.Warning",
            ProfileRestoreDisposition.PackageUnavailable => "Brush.Error",
            _ => "Brush.TextSecondary"
        });

        var title = new TextBlock
        {
            Text = requirement.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var packageKey = string.IsNullOrWhiteSpace(requirement.PackageKey)
            ? App.Services.Localization.Get("Restore.NoPackageKey")
            : requirement.PackageKey;
        var requestedVersion = NormalizeVersion(requirement.Version);
        var selectedVersion = item.SelectedVersionNumber;

        var identity = new TextBlock
        {
            Text = $"{SourceLabel(requirement.Source)}  ·  {LoaderLabel(requirement.Loader)}  ·  {packageKey}",
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var version = new TextBlock
        {
            Text = BuildVersionText(requestedVersion, selectedVersion, item.Disposition),
            Foreground = statusBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };

        var explanation = new TextBlock
        {
            Text = BuildExplanation(item),
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };

        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(title);
        labels.Children.Add(identity);
        labels.Children.Add(version);
        labels.Children.Add(explanation);

        var badge = new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 4),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = StatusLabel(item.Disposition),
                Foreground = statusBrush,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold
            }
        };

        var canInspect = item.IsInstallable &&
                         item.RemotePackage is not null &&
                         item.SelectedVersion is not null &&
                         App.Services.Environment.GameFound &&
                         !App.Services.Launcher.IsRunning(App.Services.Environment) &&
                         !App.Services.IsBusy;

        var inspect = new Button
        {
            Content = App.Services.Localization.Get("Restore.InspectInstall"),
            MinWidth = 118,
            IsVisible = item.IsInstallable,
            IsEnabled = canInspect && !item.UsesVersionFallback
        };
        inspect.Classes.Add(item.UsesVersionFallback ? "secondary" : "primary");
        inspect.Click += async (_, _) => await InspectAndInstallAsync(item);

        if (item.UsesVersionFallback)
        {
            var fallbackConfirmation = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = App.Services.Localization.Get("Restore.ConfirmFallback"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                },
                IsEnabled = canInspect
            };
            fallbackConfirmation.IsCheckedChanged += (_, _) =>
                inspect.IsEnabled = canInspect && fallbackConfirmation.IsChecked == true;
            labels.Children.Add(fallbackConfirmation);
        }

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        actions.Children.Add(badge);
        actions.Children.Add(inspect);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        row.Children.Add(labels);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        return new Border
        {
            Background = ResourceBrush("Brush.Surface"),
            BorderBrush = statusBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 10),
            Child = row
        };
    }

    private async Task InspectAndInstallAsync(ProfileRestoreItem item)
    {
        if (_refreshing || item.RemotePackage is null || item.SelectedVersion is null)
            return;

        if (App.Services.Launcher.IsRunning(App.Services.Environment))
        {
            App.Services.ReportOperation(false, App.Services.Localization.Get("Ops.GameRunningBlocked"));
            return;
        }

        SetLocalBusy(true);
        try
        {
            var installed = await PackageInspectorDialog.ShowForRemoteAsync(
                this,
                item.RemotePackage,
                item.SelectedVersion);

            if (!installed)
            {
                RenderPlan();
                return;
            }

            _changed = true;
            var current = FindCurrentProfile();
            if (current is not null && current.UnresolvedMods.Count > 0)
                App.Services.ResolveMissingProfileMods(current);

            await RefreshPlanAsync(resolveLocal: false, forceCatalogRefresh: false);
        }
        finally
        {
            SetLocalBusy(false);
        }
    }

    private void ShowCatalogError()
    {
        _plan = null;
        ReadyCountText.Text = "0";
        FallbackCountText.Text = "0";
        UnavailableCountText.Text = "0";
        ManualCountText.Text = "0";
        LoadingPanel.IsVisible = false;
        PlanPanel.IsVisible = false;
        CompletePanel.IsVisible = false;
        CatalogErrorPanel.IsVisible = true;
        FooterStatusText.Text = App.Services.Localization.Get("Restore.CatalogErrorFooter");
    }

    private void SetLocalBusy(bool busy)
    {
        LoadingText.Text = App.Services.Localization.Get("Restore.Loading");
        ResolveAgainButton.IsEnabled = !busy && !App.Services.IsBusy;
        if (busy && _plan is null)
        {
            LoadingPanel.IsVisible = true;
            CatalogErrorPanel.IsVisible = false;
            CompletePanel.IsVisible = false;
            PlanPanel.IsVisible = false;
        }
    }

    private async void ResolveAgain_Click(object? sender, RoutedEventArgs e)
        => await RefreshPlanAsync(resolveLocal: true, forceCatalogRefresh: false);

    private async void RetryCatalog_Click(object? sender, RoutedEventArgs e)
        => await RefreshPlanAsync(resolveLocal: false, forceCatalogRefresh: true);

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(_changed);

    private string BuildExplanation(ProfileRestoreItem item)
    {
        var key = item.Disposition switch
        {
            ProfileRestoreDisposition.Ready => "Restore.ExplainReady",
            ProfileRestoreDisposition.VersionFallback => "Restore.ExplainFallback",
            ProfileRestoreDisposition.PackageUnavailable => "Restore.ExplainUnavailable",
            _ => "Restore.ExplainManual"
        };

        var text = App.Services.Localization.Get(key);
        var warnings = new List<string>();
        if (item.RemotePackage?.IsDeprecated == true)
            warnings.Add(App.Services.Localization.Get("Restore.WarningDeprecated"));
        if (item.SelectedVersion is { IsActive: false })
            warnings.Add(App.Services.Localization.Get("Restore.WarningInactive"));

        return warnings.Count == 0 ? text : text + " " + string.Join(" ", warnings);
    }

    private string BuildVersionText(string? requested, string? selected, ProfileRestoreDisposition disposition)
    {
        requested ??= App.Services.Localization.Get("Restore.VersionUnknown");
        selected ??= "—";

        return disposition switch
        {
            ProfileRestoreDisposition.VersionFallback => string.Format(
                App.Services.Localization.Get("Restore.VersionFallbackText"), requested, selected),
            ProfileRestoreDisposition.Ready => string.Format(
                App.Services.Localization.Get("Restore.VersionReadyText"), requested, selected),
            _ => string.Format(App.Services.Localization.Get("Restore.VersionRequestedText"), requested)
        };
    }

    private string StatusLabel(ProfileRestoreDisposition disposition)
        => App.Services.Localization.Get(disposition switch
        {
            ProfileRestoreDisposition.Ready => "Restore.Ready",
            ProfileRestoreDisposition.VersionFallback => "Restore.VersionFallback",
            ProfileRestoreDisposition.PackageUnavailable => "Restore.Unavailable",
            _ => "Restore.Manual"
        });

    private static int SortOrder(ProfileRestoreDisposition disposition) => disposition switch
    {
        ProfileRestoreDisposition.Ready => 0,
        ProfileRestoreDisposition.VersionFallback => 1,
        ProfileRestoreDisposition.PackageUnavailable => 2,
        _ => 3
    };

    private static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var value = version.Trim();
        return value == "—" ? null : value;
    }

    private static string LoaderLabel(ModLoaderKind loader) => loader switch
    {
        ModLoaderKind.BepInEx => "BepInEx",
        ModLoaderKind.MelonLoader => "MelonLoader",
        _ => App.Services.Localization.Get("Mods.Component.Unknown")
    };

    private static string SourceLabel(ModSourceType source) => App.Services.Localization.Get(source switch
    {
        ModSourceType.Thunderstore => "Mods.Source.Thunderstore",
        ModSourceType.LocalDll => "Mods.Source.LocalDll",
        ModSourceType.Development => "Mods.Source.Development",
        ModSourceType.External => "Mods.Source.External",
        _ => "Mods.Source.LocalArchive"
    });

    private IBrush? ResourceBrush(string key) => (IBrush?)this.FindResource(key);
}
