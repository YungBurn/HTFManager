using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HTFManager.App.Views.Dialogs;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Profiles;

public partial class ProfilesView : UserControl
{
    private string? _expandedProfileName;
    private string? _deleteArmedProfileName;
    private string? _clearSnapshotArmedProfileName;
    private bool _isAttached;

    public ProfilesView()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Render();
        App.Services.Localization.LanguageChanged += (_, _) => Render();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            Render();
        };
        DetachedFromVisualTree += (_, _) => _isAttached = false;
    }

    private void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        _expandedProfileName = null;
        _deleteArmedProfileName = null;
        _clearSnapshotArmedProfileName = null;
        App.Services.SaveCurrentProfile(name, CaptureConfigSnapshotCheck.IsChecked == true);
        ProfileNameBox.Text = "";
    }

    private async void ImportProfile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = App.Services.Localization.Get("Profiles.Import"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("HTF Manager Profiles") { Patterns = new[] { "*.htfprofile", "*.htfbundle" } },
                new FilePickerFileType("HTF Manager Lightweight Profile") { Patterns = new[] { "*.htfprofile" } },
                new FilePickerFileType("HTF Manager Portable Bundle") { Patterns = new[] { "*.htfbundle" } }
            }
        });
        var file = files.FirstOrDefault();
        if (file is null || !File.Exists(file.Path.LocalPath)) return;
        if (topLevel is not Window owner) return;

        var path = file.Path.LocalPath;
        if (path.EndsWith(".htfbundle", StringComparison.OrdinalIgnoreCase))
        {
            var bundleInspection = App.Services.InspectProfileBundle(path);
            if (!bundleInspection.IsValid)
            {
                App.Services.ReportOperation(false, App.Services.Localization.Get("Ops.ProfileBundleImportFailed") + ": " + bundleInspection.Error);
                return;
            }
            await new ProfileBundleImportDialog(path, bundleInspection).ShowDialog<bool>(owner);
            return;
        }

        var inspection = App.Services.InspectProfilePackage(path);
        if (!inspection.IsValid)
        {
            App.Services.ReportOperation(false, App.Services.Localization.Get("Ops.ProfileImportFailed") + ": " + inspection.Error);
            return;
        }
        await new ProfileImportDialog(path, inspection).ShowDialog<bool>(owner);
    }

    private void Render()
    {
        if (!_isAttached || ProfileList is null) return;

        ProfileList.Children.Clear();
        var profiles = App.Services.Profiles;
        if (profiles.Count == 0)
        {
            ProfileList.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Profiles.Empty"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                Margin = new Thickness(8, 14),
                TextWrapping = TextWrapping.Wrap
            });
            _expandedProfileName = null;
            _deleteArmedProfileName = null;
            _clearSnapshotArmedProfileName = null;
            return;
        }

        if (_expandedProfileName is not null &&
            !profiles.Any(profile => profile.Name.Equals(_expandedProfileName, StringComparison.OrdinalIgnoreCase)))
            _expandedProfileName = null;

        if (_deleteArmedProfileName is not null &&
            !profiles.Any(profile => profile.Name.Equals(_deleteArmedProfileName, StringComparison.OrdinalIgnoreCase)))
            _deleteArmedProfileName = null;

        if (_clearSnapshotArmedProfileName is not null &&
            !profiles.Any(profile => profile.Name.Equals(_clearSnapshotArmedProfileName, StringComparison.OrdinalIgnoreCase)))
            _clearSnapshotArmedProfileName = null;

        foreach (var profile in profiles)
            ProfileList.Children.Add(CreateProfileCard(profile));
    }

    private Control CreateProfileCard(ModProfile profile)
    {
        var expanded = profile.Name.Equals(_expandedProfileName, StringComparison.OrdinalIgnoreCase);
        var active = profile.Name.Equals(App.Services.Settings.ActiveProfile, StringComparison.OrdinalIgnoreCase);
        var deleteArmed = profile.Name.Equals(_deleteArmedProfileName, StringComparison.OrdinalIgnoreCase);
        var health = App.Services.GetProfileHealth(profile);

        var title = new TextBlock
        {
            Text = profile.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var meta = new TextBlock
        {
            Text = ProfileSummary(profile, health) + "  ·  " + LoaderSummary(profile) + "  ·  " + HealthSummary(health),
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(title);
        labels.Children.Add(meta);

        var apply = new Button
        {
            Content = active ? App.Services.Localization.Get("Profiles.Reapply") : App.Services.Localization.Get("Profiles.Apply"),
            MinWidth = 72,
            IsEnabled = !App.Services.Launcher.IsRunning(App.Services.Environment) && !App.Services.IsBusy && health.MissingCount == 0
        };
        apply.Classes.Add(active ? "secondary" : "primary");
        apply.Click += (_, _) =>
        {
            _deleteArmedProfileName = null;
            _clearSnapshotArmedProfileName = null;
            App.Services.ApplyProfile(profile);
        };

        var export = new Button
        {
            Content = App.Services.Localization.Get("Profiles.Share"),
            MinWidth = 72,
            IsEnabled = !App.Services.IsBusy
        };
        export.Classes.Add("secondary");
        export.Click += async (_, _) => await ExportProfileAsync(profile);

        var healthButton = new Button
        {
            Content = App.Services.Localization.Get("Profiles.Health"),
            MinWidth = 72,
            IsEnabled = !App.Services.IsBusy
        };
        healthButton.Classes.Add("secondary");
        healthButton.Click += async (_, _) =>
        {
            _deleteArmedProfileName = null;
            _clearSnapshotArmedProfileName = null;
            if (TopLevel.GetTopLevel(this) is Window owner)
                await new ProfileHealthDialog(profile).ShowDialog<bool>(owner);
        };

        var delete = new Button
        {
            Content = deleteArmed
                ? App.Services.Localization.Get("Profiles.ConfirmDelete")
                : App.Services.Localization.Get("Profiles.Delete"),
            MinWidth = deleteArmed ? 110 : 86,
            IsEnabled = !App.Services.IsBusy
        };
        delete.Classes.Add("secondary");
        delete.Click += (_, _) => DeleteProfile(profile);

        var fold = new Button
        {
            Content = App.Services.Localization.Get(expanded ? "Profiles.Collapse" : "Profiles.Expand"),
            MinWidth = 72,
            IsEnabled = !App.Services.IsBusy
        };
        fold.Classes.Add("secondary");
        fold.Click += (_, _) =>
        {
            _deleteArmedProfileName = null;
            _clearSnapshotArmedProfileName = null;
            _expandedProfileName = expanded ? null : profile.Name;
            Render();
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };
        header.Children.Add(labels);
        Grid.SetColumn(apply, 1);
        header.Children.Add(apply);
        Grid.SetColumn(export, 2);
        header.Children.Add(export);
        Grid.SetColumn(healthButton, 3);
        header.Children.Add(healthButton);
        Grid.SetColumn(delete, 4);
        header.Children.Add(delete);
        Grid.SetColumn(fold, 5);
        header.Children.Add(fold);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(header);
        if (expanded)
            body.Children.Add(CreateExpandedEditor(profile));

        return new Border
        {
            Background = ResourceBrush(active ? "Brush.Elevated" : "Brush.Surface"),
            BorderBrush = ResourceBrush("Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Child = body
        };
    }

    private Control CreateExpandedEditor(ModProfile profile)
    {
        var installedById = App.Services.Mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var editor = new StackPanel { Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };

        var choices = App.Services.Mods
            .Where(mod => !profile.ModStates.ContainsKey(mod.Id))
            .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(mod => new ModChoice(mod))
            .ToArray();

        var combo = new ComboBox
        {
            PlaceholderText = App.Services.Localization.Get("Profiles.AddExistingHint"),
            ItemsSource = choices,
            SelectedIndex = choices.Length > 0 ? 0 : -1,
            MinWidth = 280
        };
        var add = new Button
        {
            Content = App.Services.Localization.Get("Profiles.AddMod"),
            MinWidth = 78,
            IsEnabled = choices.Length > 0 && !App.Services.IsBusy
        };
        add.Classes.Add("secondary");
        add.Click += (_, _) =>
        {
            _clearSnapshotArmedProfileName = null;
            if (combo.SelectedItem is ModChoice choice)
                App.Services.AddModToProfile(profile, choice.Mod);
        };

        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        addRow.Children.Add(combo);
        Grid.SetColumn(add, 1);
        addRow.Children.Add(add);
        editor.Children.Add(addRow);

        var health = App.Services.GetProfileHealth(profile);
        if (health.MissingCount > 0)
            editor.Children.Add(CreateMissingModsEditor(profile, health));

        var modList = new StackPanel { Spacing = 6 };
        foreach (var entry in profile.ModStates.OrderBy(pair => DisplayName(pair.Key, installedById), StringComparer.CurrentCultureIgnoreCase))
            modList.Children.Add(CreateProfileModRow(profile, entry.Key, entry.Value, installedById));

        if (profile.ModStates.Count == 0)
        {
            modList.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Profiles.NoMods"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                Margin = new Thickness(4, 12),
                TextWrapping = TextWrapping.Wrap
            });
        }

        editor.Children.Add(new ScrollViewer
        {
            Height = 220,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = modList
        });

        editor.Children.Add(CreateSnapshotEditor(profile, installedById));
        return editor;
    }

    private Control CreateSnapshotEditor(ModProfile profile, IReadOnlyDictionary<string, InstalledMod> installedById)
    {
        var clearArmed = profile.Name.Equals(_clearSnapshotArmedProfileName, StringComparison.OrdinalIgnoreCase);
        var title = new TextBlock
        {
            Text = App.Services.Localization.Get("Profiles.ConfigSnapshot"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 13
        };
        var meta = new TextBlock
        {
            Text = SnapshotSummary(profile),
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(title);
        labels.Children.Add(meta);

        var update = new Button
        {
            Content = profile.ConfigurationSnapshots.Count == 0
                ? App.Services.Localization.Get("Profiles.CreateSnapshot")
                : App.Services.Localization.Get("Profiles.UpdateSnapshot"),
            MinWidth = 96,
            IsEnabled = !App.Services.IsBusy && !App.Services.Launcher.IsRunning(App.Services.Environment)
        };
        update.Classes.Add("secondary");
        update.Click += (_, _) =>
        {
            _clearSnapshotArmedProfileName = null;
            App.Services.UpdateProfileConfigurationSnapshot(profile);
        };

        var clear = new Button
        {
            Content = clearArmed
                ? App.Services.Localization.Get("Profiles.ConfirmClearSnapshot")
                : App.Services.Localization.Get("Profiles.ClearSnapshot"),
            MinWidth = clearArmed ? 118 : 92,
            IsEnabled = profile.ConfigurationSnapshots.Count > 0 && !App.Services.IsBusy
        };
        clear.Classes.Add("secondary");
        clear.Click += (_, _) =>
        {
            if (!clearArmed)
            {
                _clearSnapshotArmedProfileName = profile.Name;
                Render();
                return;
            }
            _clearSnapshotArmedProfileName = null;
            App.Services.ClearProfileConfigurationSnapshot(profile);
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        header.Children.Add(labels);
        Grid.SetColumn(update, 1);
        header.Children.Add(update);
        Grid.SetColumn(clear, 2);
        header.Children.Add(clear);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = App.Services.Localization.Get("Profiles.ConfigSnapshotDescription"),
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });

        if (profile.ConfigurationSnapshots.Count > 0)
        {
            var list = new StackPanel { Spacing = 5 };
            foreach (var snapshot in profile.ConfigurationSnapshots.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var modName = snapshot.AssociatedModId is not null && installedById.TryGetValue(snapshot.AssociatedModId, out var mod)
                    ? mod.Name
                    : snapshot.DisplayName;
                var rowText = new StackPanel { Spacing = 1 };
                rowText.Children.Add(new TextBlock
                {
                    Text = modName,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var skipped = !string.IsNullOrWhiteSpace(snapshot.AssociatedModId) && !profile.ModStates.ContainsKey(snapshot.AssociatedModId);
                rowText.Children.Add(new TextBlock
                {
                    Text = $"{LoaderLabel(snapshot.Loader)}  ·  {snapshot.RelativePath}" +
                           (skipped ? "  ·  " + App.Services.Localization.Get("Profiles.SnapshotSkipped") : ""),
                    Foreground = ResourceBrush(skipped ? "Brush.Warning" : "Brush.TextSecondary"),
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                list.Children.Add(new Border
                {
                    Background = ResourceBrush("Brush.Surface"),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(9, 6),
                    Child = rowText
                });
            }

            content.Children.Add(new ScrollViewer
            {
                Height = 145,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = list
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Profiles.NoConfigSnapshot"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 10,
                Margin = new Thickness(2, 5),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            BorderBrush = ResourceBrush("Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11),
            Child = content
        };
    }

    private Control CreateProfileModRow(
        ModProfile profile,
        string modId,
        bool desiredEnabled,
        IReadOnlyDictionary<string, InstalledMod> installedById)
    {
        installedById.TryGetValue(modId, out var mod);
        var title = new TextBlock
        {
            Text = mod?.Name ?? modId,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var meta = new TextBlock
        {
            Text = mod is null
                ? App.Services.Localization.Get("Profiles.ModMissing")
                : $"{LoaderLabel(mod.Loader)}  ·  {ComponentLabel(mod.Component)}  ·  {mod.Version}",
            Foreground = ResourceBrush(mod is null ? "Brush.Warning" : "Brush.TextSecondary"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(title);
        text.Children.Add(meta);

        var state = new Button
        {
            Content = desiredEnabled ? App.Services.Localization.Get("Common.Enabled") : App.Services.Localization.Get("Common.Disabled"),
            MinWidth = 72,
            IsEnabled = !App.Services.IsBusy
        };
        state.Classes.Add("secondary");
        state.Click += (_, _) => App.Services.SetProfileModState(profile, modId, !desiredEnabled);

        var remove = new Button
        {
            Content = App.Services.Localization.Get("Profiles.RemoveMod"),
            MinWidth = 86,
            IsEnabled = !App.Services.IsBusy
        };
        remove.Classes.Add("secondary");
        remove.Click += (_, _) =>
        {
            _clearSnapshotArmedProfileName = null;
            App.Services.RemoveModFromProfile(profile, modId);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
        grid.Children.Add(text);
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        return new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7),
            Child = grid
        };
    }

    private async Task ExportProfileAsync(ModProfile profile)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || topLevel is not Window owner) return;

        var plan = await Task.Run(() => App.Services.BuildProfileBundleExportPlan(profile));
        var mode = await new ProfileShareDialog(profile, plan).ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(mode)) return;

        var full = mode.Equals("full", StringComparison.OrdinalIgnoreCase);
        var extension = full ? "htfbundle" : "htfprofile";
        var fileType = full ? "HTF Manager Portable Bundle" : "HTF Manager Profile";
        var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = App.Services.Localization.Get("Profiles.Share"),
            SuggestedFileName = SuggestedProfileFileName(profile.Name) + "." + extension,
            DefaultExtension = extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(fileType) { Patterns = new[] { "*." + extension } }
            }
        });
        if (target is null) return;

        if (full)
            await App.Services.ExportProfileBundleAsync(profile, target.Path.LocalPath);
        else
            App.Services.ExportProfile(profile, target.Path.LocalPath);
    }

    private Control CreateMissingModsEditor(ModProfile profile, ProfileHealthReport health)
    {
        var title = new TextBlock
        {
            Text = string.Format(App.Services.Localization.Get("Profiles.MissingTitle"), health.MissingCount),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12
        };
        var restore = new Button
        {
            Content = App.Services.Localization.Get("Profiles.RestoreMissing"),
            MinWidth = 128,
            IsEnabled = !App.Services.IsBusy
        };
        restore.Classes.Add("primary");
        restore.Click += async (_, _) =>
        {
            _deleteArmedProfileName = null;
            _clearSnapshotArmedProfileName = null;
            if (TopLevel.GetTopLevel(this) is Window owner)
                await ProfileRestoreDialog.ShowAsync(owner, profile);
        };

        var resolve = new Button
        {
            Content = App.Services.Localization.Get("Profiles.ResolveMissing"),
            MinWidth = 92,
            IsEnabled = !App.Services.IsBusy
        };
        resolve.Classes.Add("secondary");
        resolve.Click += (_, _) => App.Services.ResolveMissingProfileMods(profile);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        header.Children.Add(title);
        Grid.SetColumn(restore, 1);
        header.Children.Add(restore);
        Grid.SetColumn(resolve, 2);
        header.Children.Add(resolve);

        var list = new StackPanel { Spacing = 5 };
        foreach (var requirement in health.Items
                     .Where(item => item.Status == ProfileHealthStatus.Missing)
                     .Select(item => item.Expectation.Requirement)
                     .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var labels = new StackPanel { Spacing = 1 };
            labels.Children.Add(new TextBlock
            {
                Text = requirement.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            labels.Children.Add(new TextBlock
            {
                Text = $"{LoaderLabel(requirement.Loader)} · {requirement.Version}" +
                       (!string.IsNullOrWhiteSpace(requirement.PackageKey)
                           ? $" · {requirement.PackageKey}"
                           : !string.IsNullOrWhiteSpace(requirement.IntrinsicId)
                               ? $" · {requirement.IntrinsicId}"
                               : ""),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var remove = new Button
            {
                Content = App.Services.Localization.Get("Profiles.RemoveRequirement"),
                MinWidth = 96,
                IsEnabled = !App.Services.IsBusy
            };
            remove.Classes.Add("secondary");
            remove.Click += (_, _) => App.Services.RemoveMissingProfileMod(profile, requirement.PortableId);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(labels);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            list.Children.Add(new Border
            {
                Background = ResourceBrush("Brush.Surface"),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 6),
                Child = row
            });
        }

        var content = new StackPanel { Spacing = 7 };
        content.Children.Add(header);
        content.Children.Add(new TextBlock
        {
            Text = App.Services.Localization.Get("Profiles.MissingDescription"),
            Foreground = ResourceBrush("Brush.Warning"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new ScrollViewer
        {
            Height = 135,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = list
        });

        return new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            BorderBrush = ResourceBrush("Brush.Warning"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11),
            Child = content
        };
    }

    private static string SuggestedProfileFileName(string name)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safe) ? "HTF-Profile" : safe;
    }

    private void DeleteProfile(ModProfile profile)
    {
        var armed = string.Equals(_deleteArmedProfileName, profile.Name, StringComparison.OrdinalIgnoreCase);
        if (!armed)
        {
            _deleteArmedProfileName = profile.Name;
            _clearSnapshotArmedProfileName = null;
            Render();
            return;
        }

        if (_expandedProfileName?.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) == true)
            _expandedProfileName = null;
        _deleteArmedProfileName = null;
        _clearSnapshotArmedProfileName = null;
        App.Services.DeleteProfile(profile);
    }

    private string ProfileSummary(ModProfile profile, ProfileHealthReport health)
    {
        var enabledCount = profile.ModStates.Count(pair => pair.Value);
        var text = $"{profile.ModStates.Count} {App.Services.Localization.Get("Nav.Mods")}  ·  {enabledCount} {App.Services.Localization.Get("Common.Enabled")}";
        if (profile.ConfigurationSnapshots.Count > 0)
            text += $"  ·  {profile.ConfigurationSnapshots.Count} {App.Services.Localization.Get("Profiles.ConfigSnapshotsShort")}";
        if (health.MissingCount > 0)
            text += $"  ·  {health.MissingCount} {App.Services.Localization.Get("Profiles.MissingShort")}";
        return text;
    }

    private string HealthSummary(ProfileHealthReport health)
    {
        if (health.MissingCount > 0)
            return $"{health.MissingCount} {App.Services.Localization.Get("Health.MissingShort")}";
        if (health.VersionMismatchCount > 0)
            return $"{health.VersionMismatchCount} {App.Services.Localization.Get("Health.DriftShort")}";
        if (health.IdentityUncertainCount > 0)
            return $"{health.IdentityUncertainCount} {App.Services.Localization.Get("Health.UncertainShort")}";
        return App.Services.Localization.Get("Health.HealthyShort");
    }

    private string SnapshotSummary(ModProfile profile)
    {
        if (profile.ConfigurationSnapshots.Count == 0)
            return App.Services.Localization.Get("Profiles.NoConfigSnapshotShort");

        var timestamp = profile.ConfigurationSnapshotCapturedUtc?.ToLocalTime().ToString("g") ?? "—";
        return $"{profile.ConfigurationSnapshots.Count} {App.Services.Localization.Get("Profiles.ConfigFiles")}  ·  {App.Services.Localization.Get("Profiles.CapturedAt")} {timestamp}";
    }

    private string LoaderSummary(ModProfile profile)
    {
        var ids = profile.ModStates.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaders = App.Services.Mods
            .Where(mod => ids.Contains(mod.Id) && mod.Loader != ModLoaderKind.Unknown)
            .Select(mod => mod.Loader)
            .Concat(profile.UnresolvedMods.Where(item => item.Loader != ModLoaderKind.Unknown).Select(item => item.Loader))
            .Distinct()
            .ToArray();

        if (loaders.Length == 0) return App.Services.Localization.Get("Profiles.LoaderNone");
        if (loaders.Length > 1) return App.Services.Localization.Get("Profiles.LoaderMixed");
        return LoaderLabel(loaders[0]);
    }

    private string ComponentLabel(ModComponentKind component) => component switch
    {
        ModComponentKind.Mod => App.Services.Localization.Get("Mods.Component.Mod"),
        ModComponentKind.Plugin => App.Services.Localization.Get("Mods.Component.Plugin"),
        ModComponentKind.Patcher => App.Services.Localization.Get("Mods.Component.Patcher"),
        ModComponentKind.Content => App.Services.Localization.Get("Mods.Component.Content"),
        ModComponentKind.Modpack => App.Services.Localization.Get("Discover.Modpacks"),
        _ => App.Services.Localization.Get("Mods.Component.Unknown")
    };

    private static string DisplayName(string modId, IReadOnlyDictionary<string, InstalledMod> installedById)
        => installedById.TryGetValue(modId, out var mod) ? mod.Name : modId;

    private static string LoaderLabel(ModLoaderKind loader) => loader switch
    {
        ModLoaderKind.BepInEx => "BepInEx",
        ModLoaderKind.MelonLoader => "MelonLoader",
        _ => "Unknown"
    };

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;

    private sealed class ModChoice
    {
        public ModChoice(InstalledMod mod) => Mod = mod;
        public InstalledMod Mod { get; }
        public override string ToString() => $"{Mod.Name}  ·  {LoaderLabel(Mod.Loader)}  ·  {Mod.Version}";
    }
}
