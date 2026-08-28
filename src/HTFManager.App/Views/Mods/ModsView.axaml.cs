using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HTFManager.Core.Models;
using HTFManager.App.Views.Dialogs;

namespace HTFManager.App.Views.Mods;

public partial class ModsView : UserControl
{
    private bool _isAttached;

    public ModsView()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Render();
        App.Services.Localization.LanguageChanged += (_, _) =>
        {
            RefreshLoaderFilter();
            RefreshOwnershipFilter();
            Render();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            Render();
        };
        DetachedFromVisualTree += (_, _) => _isAttached = false;
        RefreshLoaderFilter();
        RefreshOwnershipFilter();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => Render();
    private void LoaderFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e) => Render();
    private void OwnershipFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e) => Render();
    private void Refresh_Click(object? sender, RoutedEventArgs e) => App.Services.Refresh();
    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e) => await App.Services.LoadCatalogAsync();

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = App.Services.Localization.Get("Mods.Import"),
            AllowMultiple = true
        });

        var paths = files.Select(f => f.Path.LocalPath).Where(File.Exists).ToArray();
        if (paths.Length == 0) return;
        if (topLevel is not Window owner || !App.Services.Settings.ShowPackageInspector)
        {
            await App.Services.InstallLocalFilesAsync(paths);
            return;
        }
        foreach (var path in paths)
            await PackageInspectorDialog.ShowForLocalAsync(owner, path);
    }

    private void Render()
    {
        if (!_isAttached || ModList is null) return;
        ModList.Children.Clear();

        var query = SearchBox?.Text?.Trim() ?? "";
        var loaderFilter = LoaderFilterCombo?.SelectedIndex ?? 0;
        var ownershipFilter = OwnershipFilterCombo?.SelectedIndex ?? 0;

        var mods = App.Services.Mods
            .Where(m => loaderFilter == 0 ||
                        (loaderFilter == 1 && m.Loader == ModLoaderKind.BepInEx) ||
                        (loaderFilter == 2 && m.Loader == ModLoaderKind.MelonLoader))
            .Where(m => ownershipFilter == 0 ||
                        (ownershipFilter == 1 && m.IsManaged) ||
                        (ownershipFilter == 2 && !m.IsManaged))
            .Where(m => string.IsNullOrWhiteSpace(query) ||
                        m.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        m.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        m.FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        SourceLabel(m.Source).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (mods.Length == 0)
        {
            ModList.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Mods.Empty"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                Margin = new Thickness(4, 18)
            });
            return;
        }

        foreach (var mod in mods)
            ModList.Children.Add(CreateModRow(mod));
    }

    private Control CreateModRow(InstalledMod mod)
    {
        var managementColor = mod.IsManaged ? "Brush.Primary" : "Brush.Warning";

        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = ResourceBrush("Brush.Elevated"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new PathIcon
            {
                Data = this.FindResource("Icon.Package") as Geometry,
                Width = 19,
                Height = 19,
                Foreground = ResourceBrush(managementColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var identity = new StackPanel { Spacing = 1 };
        identity.Children.Add(new TextBlock
        {
            Text = mod.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (ShouldShowAuthor(mod.Author))
        {
            identity.Children.Add(new TextBlock
            {
                Text = mod.Author,
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var statusBadges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        statusBadges.Children.Add(CreateBadge(
            mod.Enabled ? App.Services.Localization.Get("Common.Enabled") : App.Services.Localization.Get("Common.Disabled"),
            mod.Enabled ? "Brush.Success" : "Brush.TextSecondary"));

        if (mod.UpdateAvailable)
        {
            var updateText = App.Services.Localization.Get("Mods.UpdateAvailable");
            if (!string.IsNullOrWhiteSpace(mod.LatestVersion))
                updateText += " · " + FormatVersion(mod.LatestVersion!);
            statusBadges.Children.Add(CreateBadge(updateText, "Brush.Primary"));
        }

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(identity);
        Grid.SetColumn(statusBadges, 1);
        header.Children.Add(statusBadges);

        var badges = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        badges.Children.Add(CreateBadge(FormatVersion(mod.Version), "Brush.TextSecondary"));
        badges.Children.Add(CreateBadge(LoaderLabel(mod.Loader), LoaderColor(mod.Loader)));
        badges.Children.Add(CreateBadge(ComponentLabel(mod.Component), "Brush.TextSecondary"));
        badges.Children.Add(CreateBadge(SourceLabel(mod.Source), SourceColor(mod.Source)));
        badges.Children.Add(CreateBadge(
            mod.IsManaged ? App.Services.Localization.Get("Common.Managed") : App.Services.Localization.Get("Common.External"),
            managementColor));

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);
        body.Children.Add(badges);

        if (!string.IsNullOrWhiteSpace(mod.Description))
        {
            body.Children.Add(new TextBlock
            {
                Text = mod.Description,
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var actions = CreateActions(mod);
        body.Children.Add(actions);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        grid.Children.Add(icon);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(14),
            BorderBrush = ResourceBrush(mod.IsManaged ? "Brush.Border" : "Brush.Warning"),
            BorderThickness = new Thickness(1)
        };
        border.Classes.Add("card");
        return border;
    }

    private StackPanel CreateActions(InstalledMod mod)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        };

        if (mod.UpdateAvailable && !string.IsNullOrWhiteSpace(mod.PackageKey))
        {
            var update = new Button
            {
                Content = App.Services.Localization.Get("Common.Update"),
                MinWidth = 66,
                IsEnabled = !App.Services.IsBusy
            };
            update.Classes.Add("primary");
            update.Click += async (_, _) => await App.Services.UpdateModAsync(mod);
            actions.Children.Add(update);
        }

        var configuration = App.Services.FindConfigurationForMod(mod);
        if (configuration is not null)
        {
            var configure = new Button
            {
                Content = App.Services.Localization.Get("Config.Configure"),
                MinWidth = 72,
                IsEnabled = !App.Services.IsBusy
            };
            configure.Classes.Add("secondary");
            configure.Click += (_, _) => App.Services.RequestConfiguration(configuration);
            actions.Children.Add(configure);
        }

        var open = new Button
        {
            Content = App.Services.Localization.Get("Mods.OpenFolder"),
            IsEnabled = !App.Services.IsBusy
        };
        open.Classes.Add("secondary");
        open.Click += (_, _) => App.Services.OpenModFolder(mod);
        actions.Children.Add(open);

        var canToggle = !mod.IsManaged || mod.OwnedFiles.Any(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (canToggle)
        {
            var toggle = new Button
            {
                Content = mod.Enabled ? App.Services.Localization.Get("Mods.Disable") : App.Services.Localization.Get("Mods.Enable"),
                MinWidth = 72,
                IsEnabled = !App.Services.Launcher.IsRunning(App.Services.Environment) && !App.Services.IsBusy
            };
            toggle.Classes.Add("secondary");
            toggle.Click += (_, _) => App.Services.ToggleMod(mod);
            actions.Children.Add(toggle);
        }

        if (mod.IsManaged)
        {
            var armed = false;
            var uninstall = new Button
            {
                Content = App.Services.Localization.Get("Mods.Uninstall"),
                MinWidth = 76,
                IsEnabled = !App.Services.IsBusy
            };
            uninstall.Classes.Add("secondary");
            uninstall.Click += (_, _) =>
            {
                if (!armed)
                {
                    armed = true;
                    uninstall.Content = App.Services.Localization.Get("Mods.ConfirmUninstall");
                    return;
                }

                App.Services.UninstallMod(mod);
            };
            actions.Children.Add(uninstall);
        }

        return actions;
    }

    private Border CreateBadge(string text, string foregroundKey)
    {
        return new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            BorderBrush = ResourceBrush(foregroundKey),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 2),
            Margin = new Thickness(0, 0, 6, 5),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 9,
                Foreground = ResourceBrush(foregroundKey),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void RefreshLoaderFilter()
    {
        if (LoaderFilterCombo is null) return;
        var selected = LoaderFilterCombo.SelectedIndex < 0 ? 0 : LoaderFilterCombo.SelectedIndex;
        LoaderFilterCombo.ItemsSource = new[]
        {
            App.Services.Localization.Get("Mods.Filter.AllLoaders"),
            "BepInEx",
            "MelonLoader"
        };
        LoaderFilterCombo.SelectedIndex = Math.Min(selected, 2);
    }

    private void RefreshOwnershipFilter()
    {
        if (OwnershipFilterCombo is null) return;
        var selected = OwnershipFilterCombo.SelectedIndex < 0 ? 0 : OwnershipFilterCombo.SelectedIndex;
        OwnershipFilterCombo.ItemsSource = new[]
        {
            App.Services.Localization.Get("Mods.Filter.AllOwnership"),
            App.Services.Localization.Get("Common.Managed"),
            App.Services.Localization.Get("Common.External")
        };
        OwnershipFilterCombo.SelectedIndex = Math.Min(selected, 2);
    }

    private static bool ShouldShowAuthor(string author)
        => !string.IsNullOrWhiteSpace(author) &&
           !author.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
           !author.Equals("Local / External", StringComparison.OrdinalIgnoreCase);

    private static string FormatVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version == "—") return "v—";
        return version.StartsWith('v') || version.StartsWith('V') ? version : "v" + version;
    }

    private static string LoaderLabel(ModLoaderKind loader) => loader switch
    {
        ModLoaderKind.BepInEx => "BepInEx",
        ModLoaderKind.MelonLoader => "MelonLoader",
        _ => "Unknown"
    };

    private static string LoaderColor(ModLoaderKind loader) => loader switch
    {
        ModLoaderKind.BepInEx => "Brush.Primary",
        ModLoaderKind.MelonLoader => "Brush.Warning",
        _ => "Brush.TextSecondary"
    };

    private string ComponentLabel(ModComponentKind component) => component switch
    {
        ModComponentKind.Mod => App.Services.Localization.Get("Mods.Component.Mod"),
        ModComponentKind.Plugin => App.Services.Localization.Get("Mods.Component.Plugin"),
        ModComponentKind.Patcher => App.Services.Localization.Get("Mods.Component.Patcher"),
        ModComponentKind.Content => App.Services.Localization.Get("Mods.Component.Content"),
        ModComponentKind.Modpack => App.Services.Localization.Get("Discover.Modpacks"),
        _ => App.Services.Localization.Get("Mods.Component.Unknown")
    };

    private string SourceLabel(ModSourceType source) => source switch
    {
        ModSourceType.Thunderstore => App.Services.Localization.Get("Mods.Source.Thunderstore"),
        ModSourceType.LocalArchive => App.Services.Localization.Get("Mods.Source.LocalArchive"),
        ModSourceType.LocalDll => App.Services.Localization.Get("Mods.Source.LocalDll"),
        ModSourceType.Development => App.Services.Localization.Get("Mods.Source.Development"),
        _ => App.Services.Localization.Get("Mods.Source.External")
    };

    private static string SourceColor(ModSourceType source) => source switch
    {
        ModSourceType.Thunderstore => "Brush.Primary",
        ModSourceType.Development => "Brush.Warning",
        ModSourceType.External => "Brush.Warning",
        _ => "Brush.TextSecondary"
    };

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;
}
