using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HTFManager.Core.Models;
using HTFManager.App.Views.Dialogs;

namespace HTFManager.App.Views.Discover;

public partial class DiscoverView : UserControl
{
    private bool _updatingCombos;
    private bool _isAttached;

    public DiscoverView()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Render();
        App.Services.Localization.LanguageChanged += (_, _) =>
        {
            ConfigureCombos();
            Render();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            Render();
        };
        DetachedFromVisualTree += (_, _) => _isAttached = false;
        ConfigureCombos();
    }

    public async Task ActivateAsync()
    {
        if (App.Services.CatalogLoaded)
        {
            Render();
            return;
        }
        await EnsureCatalogAsync();
    }

    private async Task EnsureCatalogAsync(bool forceRefresh = false)
    {
        LoadingBar.IsVisible = true;
        CatalogStatusText.Text = App.Services.Localization.Get("Discover.Loading");
        if (!App.Services.CatalogLoaded || forceRefresh)
            await App.Services.LoadCatalogAsync(forceRefresh);
        LoadingBar.IsVisible = false;
        Render();
    }

    private void ConfigureCombos()
    {
        _updatingCombos = true;
        var filter = FilterCombo.SelectedIndex < 0 ? 0 : FilterCombo.SelectedIndex;
        var sort = SortCombo.SelectedIndex < 0 ? 0 : SortCombo.SelectedIndex;

        FilterCombo.ItemsSource = new[]
        {
            App.Services.Localization.Get("Discover.All"),
            App.Services.Localization.Get("Discover.Mods"),
            App.Services.Localization.Get("Discover.Libraries"),
            App.Services.Localization.Get("Discover.Tools"),
            App.Services.Localization.Get("Discover.Modpacks")
        };
        SortCombo.ItemsSource = new[]
        {
            App.Services.Localization.Get("Discover.SortUpdated"),
            App.Services.Localization.Get("Discover.SortDownloads"),
            App.Services.Localization.Get("Discover.SortRating")
        };
        FilterCombo.SelectedIndex = Math.Clamp(filter, 0, 4);
        SortCombo.SelectedIndex = Math.Clamp(sort, 0, 2);
        _updatingCombos = false;
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => Render();
    private void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { if (!_updatingCombos) Render(); }
    private void SortCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) { if (!_updatingCombos) Render(); }
    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await EnsureCatalogAsync(true);

    private void Render()
    {
        if (!_isAttached || PackageList is null) return;
        PackageList.Children.Clear();

        if (!App.Services.CatalogLoaded)
        {
            CatalogStatusText.Text = App.Services.IsBusy
                ? App.Services.Localization.Get("Discover.Loading")
                : App.Services.Localization.Get("Discover.Failed");
            return;
        }

        var query = SearchBox?.Text?.Trim() ?? "";
        var category = FilterCombo.SelectedIndex switch
        {
            1 => "Mods",
            2 => "Libraries",
            3 => "Tools",
            4 => "Modpacks",
            _ => null
        };

        IEnumerable<RemoteModPackage> packages = App.Services.Catalog;
        packages = packages.Where(p => !p.HasNsfwContent);
        if (App.Services.Settings.HideDeprecatedPackages)
            packages = packages.Where(p => !p.IsDeprecated);
        if (category is not null)
            packages = packages.Where(p => p.Categories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(query))
        {
            packages = packages.Where(p =>
                p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                p.Owner.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (p.LatestVersion?.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        packages = SortCombo.SelectedIndex switch
        {
            1 => packages.OrderByDescending(p => p.TotalDownloads),
            2 => packages.OrderByDescending(p => p.RatingScore).ThenByDescending(p => p.TotalDownloads),
            _ => packages.OrderByDescending(p => p.DateUpdated)
        };

        var result = packages.ToArray();
        CatalogStatusText.Text = $"{result.Length} {App.Services.Localization.Get("Discover.PackageCount")}";

        if (result.Length == 0)
        {
            PackageList.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Discover.Empty"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                Margin = new Thickness(4, 18)
            });
            return;
        }

        foreach (var package in result)
            PackageList.Children.Add(CreatePackageCard(package));
    }

    private Control CreatePackageCard(RemoteModPackage package)
    {
        var latest = package.LatestVersion;
        var installed = App.Services.Mods.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.PackageKey) &&
            m.PackageKey.Equals(package.FullName, StringComparison.OrdinalIgnoreCase));

        var card = new Border
        {
            Width = 350,
            MinHeight = 215,
            Margin = new Thickness(0, 0, 12, 12)
        };
        card.Classes.Add("card");

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(9),
            Background = ResourceBrush("Brush.Elevated"),
            Child = new PathIcon
            {
                Data = this.FindResource("Icon.Package") as Geometry,
                Width = 19,
                Height = 19,
                Foreground = ResourceBrush("Brush.Primary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(icon);

        var titleStack = new StackPanel { Spacing = 3 };
        titleStack.Children.Add(new TextBlock
        {
            Text = package.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var categoryText = package.Categories.Count == 0 ? "" : "  ·  " + string.Join(" / ", package.Categories.Take(2));
        titleStack.Children.Add(new TextBlock
        {
            Text = package.Owner + categoryText,
            FontSize = 11,
            Foreground = ResourceBrush("Brush.TextSecondary")
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);
        root.Children.Add(header);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 13, 0, 8)
        };
        meta.Children.Add(new TextBlock { Text = latest?.VersionNumber ?? "—", FontSize = 11, Foreground = ResourceBrush("Brush.Primary") });
        meta.Children.Add(new TextBlock { Text = FormatDownloads(package.TotalDownloads), FontSize = 11, Foreground = ResourceBrush("Brush.TextSecondary") });
        if (package.IsDeprecated)
            meta.Children.Add(new TextBlock { Text = App.Services.Localization.Get("Discover.Deprecated"), FontSize = 11, Foreground = ResourceBrush("Brush.Warning") });
        Grid.SetRow(meta, 1);
        root.Children.Add(meta);

        var description = new TextBlock
        {
            Text = latest?.Description ?? "",
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 58,
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(description, 2);
        root.Children.Add(description);

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetRow(actions, 3);
        var open = new Button { Classes = { "secondary" } };
        open.Content = App.Services.Localization.Get("Discover.OpenPage");
        open.Click += (_, _) => App.Services.OpenPackagePage(package);
        actions.Children.Add(open);

        var install = new Button { MinWidth = 92 };
        var isBepInExPackage = package.FullName.Equals("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase) ||
                               package.Name.Equals("BepInExPack", StringComparison.OrdinalIgnoreCase);
        if (isBepInExPackage)
        {
            if (App.Services.Environment.BepInEx.Healthy)
            {
                install.Content = App.Services.Localization.Get("Discover.Installed");
                install.IsEnabled = false;
                install.Classes.Add("secondary");
            }
            else
            {
                install.Content = App.Services.Localization.Get("Discover.SetupLoader");
                install.IsEnabled = !App.Services.IsBusy;
                install.Classes.Add("primary");
                install.Click += async (_, _) =>
                {
                    if (TopLevel.GetTopLevel(this) is Window owner)
                        await new LoaderSetupDialog(ModLoaderKind.BepInEx).ShowDialog<bool>(owner);
                };
            }
        }
        else if (installed is not null && !installed.UpdateAvailable)
        {
            install.Content = App.Services.Localization.Get("Discover.Installed");
            install.IsEnabled = false;
            install.Classes.Add("secondary");
        }
        else
        {
            install.Content = installed?.UpdateAvailable == true
                ? App.Services.Localization.Get("Discover.Update")
                : App.Services.Localization.Get("Discover.Install");
            install.Classes.Add("primary");
            install.IsEnabled = !App.Services.IsBusy && latest is not null;
            install.Click += async (_, _) =>
            {
                if (!App.Services.Settings.ShowPackageInspector || TopLevel.GetTopLevel(this) is not Window owner)
                {
                    await App.Services.InstallRemotePackageAsync(package);
                    return;
                }
                await PackageInspectorDialog.ShowForRemoteAsync(owner, package);
            };
        }
        Grid.SetColumn(install, 1);
        actions.Children.Add(install);
        root.Children.Add(actions);

        card.Child = root;
        return card;
    }

    private static string FormatDownloads(long value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#}M ↓";
        if (value >= 1_000) return $"{value / 1_000d:0.#}K ↓";
        return $"{value} ↓";
    }

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;
}
