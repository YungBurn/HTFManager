using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class ProfileRestoreDialog : Window
{
    private string _profileName = "";
    private string? _bundlePath;
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
        _bundlePath = App.Services.GetProfileBundleSource(profile);
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
            _bundlePath ??= App.Services.GetProfileBundleSource(profile);

            if (resolveLocal)
            {
                // ResolveMissingMods also rebuilds UnresolvedMods from ExpectedMods, so this must
                // run even when the legacy projection is currently empty (for example, a Mod
                // removed after the profile was captured).
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

            IReadOnlyList<HtfBundlePayloadDescriptor> bundledPayloads = Array.Empty<HtfBundlePayloadDescriptor>();
            if (!string.IsNullOrWhiteSpace(_bundlePath) && File.Exists(_bundlePath))
            {
                var bundleInspection = App.Services.InspectProfileBundle(_bundlePath);
                if (bundleInspection.IsValid && bundleInspection.Manifest is not null)
                    bundledPayloads = bundleInspection.Manifest.Payloads;
            }

            // Build a bundle/manual-only plan first. If every unresolved requirement can be
            // classified without Thunderstore, avoid a network request entirely so a complete
            // portable bundle remains useful offline.
            var offlinePlan = App.Services.ProfileRestoreService.BuildPlan(
                profile,
                Array.Empty<RemoteModPackage>(),
                bundledPayloads,
                catalogAvailable: false);
            var needsCatalog = offlinePlan.Items.Any(item =>
                item.Disposition == ProfileRestoreDisposition.CatalogUnavailable);

            if (needsCatalog && (!App.Services.CatalogLoaded || forceCatalogRefresh))
                await App.Services.LoadCatalogAsync(forceCatalogRefresh);

            var catalogAvailable = App.Services.CatalogLoaded;
            var hasBundleContext = !string.IsNullOrWhiteSpace(_bundlePath) && File.Exists(_bundlePath);
            if (needsCatalog && !catalogAvailable && !hasBundleContext)
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

            _plan = needsCatalog
                ? App.Services.ProfileRestoreService.BuildPlan(
                    profile,
                    catalogAvailable ? App.Services.Catalog : Array.Empty<RemoteModPackage>(),
                    bundledPayloads,
                    catalogAvailable)
                : offlinePlan;
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
            ProfileRestoreDisposition.PackageUnavailable or ProfileRestoreDisposition.CatalogUnavailable => "Brush.Error",
            _ => "Brush.TextSecondary"
        });

        var title = new TextBlock
        {
            Text = requirement.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var packageKey = !string.IsNullOrWhiteSpace(requirement.PackageKey)
            ? requirement.PackageKey
            : !string.IsNullOrWhiteSpace(requirement.IntrinsicId)
                ? requirement.IntrinsicId
                : App.Services.Localization.Get("Restore.NoPackageKey");
        var requestedVersion = NormalizeVersion(requirement.Version);
        var selectedVersion = item.SelectedVersionNumber;
        var recoverySource = item.RestoreSource == ProfileRestoreSource.Bundle
            ? App.Services.Localization.Get("Restore.SourceBundle")
            : SourceLabel(requirement.Source);

        var identity = new TextBlock
        {
            Text = $"{recoverySource}  ·  {LoaderLabel(requirement.Loader)}  ·  {packageKey}",
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

        var hasSource = item.RestoreSource switch
        {
            ProfileRestoreSource.Bundle => item.BundlePayload is not null && !string.IsNullOrWhiteSpace(_bundlePath),
            ProfileRestoreSource.Thunderstore => item.RemotePackage is not null && item.SelectedVersion is not null,
            _ => false
        };
        var canInspect = item.IsInstallable &&
                         hasSource &&
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
        if (_refreshing || !item.IsInstallable)
            return;

        if (App.Services.Launcher.IsRunning(App.Services.Environment))
        {
            App.Services.ReportOperation(false, App.Services.Localization.Get("Ops.GameRunningBlocked"));
            return;
        }

        SetLocalBusy(true);
        try
        {
            bool installed;
            if (item.RestoreSource == ProfileRestoreSource.Bundle &&
                item.BundlePayload is not null &&
                !string.IsNullOrWhiteSpace(_bundlePath))
            {
                installed = await PackageInspectorDialog.ShowForBundledAsync(
                    this,
                    _bundlePath!,
                    item.BundlePayload,
                    item.Requirement);
            }
            else if (item.RemotePackage is not null && item.SelectedVersion is not null)
            {
                installed = await PackageInspectorDialog.ShowForRemoteAsync(
                    this,
                    item.RemotePackage,
                    item.SelectedVersion);
            }
            else
            {
                return;
            }

            if (!installed)
            {
                RenderPlan();
                return;
            }

            _changed = true;
            var current = FindCurrentProfile();
            if (current is not null)
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
        if (item.RestoreSource == ProfileRestoreSource.Bundle)
            return App.Services.Localization.Get("Restore.ExplainBundle");

        var key = item.Disposition switch
        {
            ProfileRestoreDisposition.Ready => "Restore.ExplainReady",
            ProfileRestoreDisposition.VersionFallback => "Restore.ExplainFallback",
            ProfileRestoreDisposition.PackageUnavailable => "Restore.ExplainUnavailable",
            ProfileRestoreDisposition.CatalogUnavailable => "Restore.ExplainCatalogUnavailable",
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
            ProfileRestoreDisposition.PackageUnavailable or ProfileRestoreDisposition.CatalogUnavailable => "Restore.Unavailable",
            _ => "Restore.Manual"
        });

    private static int SortOrder(ProfileRestoreDisposition disposition) => disposition switch
    {
        ProfileRestoreDisposition.Ready => 0,
        ProfileRestoreDisposition.VersionFallback => 1,
        ProfileRestoreDisposition.PackageUnavailable or ProfileRestoreDisposition.CatalogUnavailable => 2,
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
