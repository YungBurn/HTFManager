using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Configuration;

public partial class ConfigurationView : UserControl
{
    private const int CurrentSafetyNoticeVersion = 1;

    private bool _isAttached;
    private bool _loadingSectionFilter;
    private string? _selectedId;
    private bool _restoreArmed;

    public ConfigurationView()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Render();
        App.Services.Localization.LanguageChanged += (_, _) =>
        {
            RefreshLoaderFilter();
            Render();
        };
        App.Services.ConfigurationRequested += (_, _) =>
        {
            _selectedId = App.Services.RequestedConfigurationId;
            Render();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            if (!string.IsNullOrWhiteSpace(App.Services.RequestedConfigurationId))
                _selectedId = App.Services.RequestedConfigurationId;
            RefreshLoaderFilter();
            Render();
        };
        DetachedFromVisualTree += (_, _) => _isAttached = false;
        RefreshLoaderFilter();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => RenderList();
    private void LoaderFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RenderList();

    private void SectionFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSectionFilter) return;
        RenderEntries();
    }

    private void Render()
    {
        if (!_isAttached || ConfigurationList is null) return;
        var documents = App.Services.Configurations;
        if (_selectedId is not null && documents.All(document => !document.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase)))
            _selectedId = null;
        if (_selectedId is null && !string.IsNullOrWhiteSpace(App.Services.RequestedConfigurationId) &&
            documents.Any(document => document.Id.Equals(App.Services.RequestedConfigurationId, StringComparison.OrdinalIgnoreCase)))
            _selectedId = App.Services.RequestedConfigurationId;

        UpdateSafetyOverlay();
        RenderList();
        RenderDetails();
    }

    private void UpdateSafetyOverlay()
    {
        if (SafetyOverlay is null) return;
        SafetyOverlay.IsVisible = App.Services.Settings.AcknowledgedConfigSafetyVersion < CurrentSafetyNoticeVersion;
    }

    private void SafetyAcknowledge_Click(object? sender, RoutedEventArgs e)
    {
        App.Services.Settings.AcknowledgedConfigSafetyVersion = CurrentSafetyNoticeVersion;
        App.Services.SaveSettings();
        SafetyOverlay.IsVisible = false;
    }

    private void RenderList()
    {
        if (!_isAttached || ConfigurationList is null) return;
        ConfigurationList.Children.Clear();

        var query = SearchBox?.Text?.Trim() ?? "";
        var loaderFilter = LoaderFilterCombo?.SelectedIndex ?? 0;
        var documents = App.Services.Configurations
            .Where(document => loaderFilter == 0 ||
                               (loaderFilter == 1 && document.Loader == ModLoaderKind.BepInEx) ||
                               (loaderFilter == 2 && document.Loader == ModLoaderKind.MelonLoader) ||
                               (loaderFilter == 3 && document.IsLoaderConfiguration))
            .Where(document => string.IsNullOrWhiteSpace(query) ||
                               document.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                               document.FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                               (document.PluginGuid?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                               document.Entries.Any(entry => EntryMatchesQuery(document, entry, query)))
            .ToArray();

        if (documents.Length == 0)
        {
            ConfigurationList.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Config.NoFiles"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                Margin = new Thickness(4, 12),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var document in documents)
            ConfigurationList.Children.Add(CreateDocumentButton(document));
    }

    private bool EntryMatchesQuery(ModConfigurationDocument document, ConfigurationEntry entry, string query)
    {
        if (entry.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            entry.Section.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            entry.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            return true;

        var localized = App.Services.ConfigLocalization.Resolve(document, entry, App.Services.Localization.CurrentLanguage);
        return localized.HasLocalization &&
               (localized.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                localized.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                localized.SectionTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private Control CreateDocumentButton(ModConfigurationDocument document)
    {
        var selected = string.Equals(_selectedId, document.Id, StringComparison.OrdinalIgnoreCase);
        var developerMode = App.Services.Settings.DeveloperMode;
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(11),
            Background = ResourceBrush(selected ? "Brush.Elevated" : "Brush.Surface"),
            BorderBrush = ResourceBrush(selected ? "Brush.Primary" : "Brush.Border"),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) =>
        {
            _selectedId = document.Id;
            _restoreArmed = false;
            Render();
        };

        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock
        {
            Text = document.DisplayName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{LoaderLabel(document.Loader)} · {document.Entries.Count} {App.Services.Localization.Get("Config.SettingsCount")}",
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 10
        });
        stack.Children.Add(new TextBlock
        {
            Text = App.Services.Localization.Get(document.IsLoaderConfiguration ? "Config.LoaderConfig" : "Config.ModConfig"),
            Foreground = ResourceBrush(document.IsLoaderConfiguration ? "Brush.Warning" : "Brush.Primary"),
            FontSize = 10
        });

        if (developerMode)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(document.FilePath),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        button.Content = stack;
        return button;
    }

    private void RenderDetails()
    {
        var document = SelectedDocument();
        EmptyPanel.IsVisible = document is null;
        DetailPanel.IsVisible = document is not null;
        if (document is null) return;

        var developerMode = App.Services.Settings.DeveloperMode;
        ConfigTitleText.Text = document.DisplayName;
        ConfigPathText.Text = document.FilePath;
        ConfigPathText.IsVisible = developerMode;
        ConfigHintText.Text = App.Services.Localization.Get(document.IsLoaderConfiguration
            ? "Config.LoaderCaution"
            : "Config.RestartHint");

        HeaderBadges.Children.Clear();
        HeaderBadges.Children.Add(CreateBadge(LoaderLabel(document.Loader), LoaderColor(document.Loader)));
        HeaderBadges.Children.Add(CreateBadge(
            App.Services.Localization.Get(document.IsLoaderConfiguration ? "Config.LoaderConfig" : "Config.ModConfig"),
            document.IsLoaderConfiguration ? "Brush.Warning" : "Brush.Primary"));
        if (!string.IsNullOrWhiteSpace(document.DetectedVersion))
            HeaderBadges.Children.Add(CreateBadge("v" + document.DetectedVersion!.TrimStart('v', 'V'), "Brush.TextSecondary"));
        if (developerMode && !string.IsNullOrWhiteSpace(document.PluginGuid))
            HeaderBadges.Children.Add(CreateBadge(document.PluginGuid!, "Brush.TextSecondary"));

        RefreshSectionFilter(document);
        var backups = App.Services.GetConfigurationBackups(document);
        BackupCountText.Text = string.Format(App.Services.Localization.Get("Config.BackupCount"), backups.Count);
        RestoreButton.IsEnabled = backups.Count > 0 && !App.Services.IsBusy && !App.Services.Launcher.IsRunning(App.Services.Environment);
        RestoreButtonText.Text = App.Services.Localization.Get(_restoreArmed ? "Config.ConfirmRestore" : "Config.Restore");
        RenderEntries();
        UpdateDirtyState(document);
    }

    private void RefreshSectionFilter(ModConfigurationDocument document)
    {
        var oldRaw = (SectionFilterCombo.SelectedItem as SectionFilterOption)?.Raw;
        _loadingSectionFilter = true;
        var items = new List<SectionFilterOption>
        {
            new("", App.Services.Localization.Get("Config.AllSections"))
        };

        foreach (var section in document.Sections)
        {
            var localized = App.Services.ConfigLocalization.GetSectionTitle(
                document,
                section,
                App.Services.Localization.CurrentLanguage);
            var display = App.Services.Settings.DeveloperMode && !localized.Equals(section, StringComparison.Ordinal)
                ? localized + " · " + section
                : localized;
            items.Add(new SectionFilterOption(section, display));
        }

        SectionFilterCombo.ItemsSource = items;
        var selected = oldRaw is not null
            ? items.FindIndex(item => item.Raw.Equals(oldRaw, StringComparison.CurrentCultureIgnoreCase))
            : -1;
        SectionFilterCombo.SelectedIndex = selected >= 0 ? selected : 0;
        _loadingSectionFilter = false;
    }

    private void RenderEntries()
    {
        if (!_isAttached || EntryList is null) return;
        EntryList.Children.Clear();
        var document = SelectedDocument();
        if (document is null) return;

        var selectedSection = SectionFilterCombo.SelectedIndex > 0 && SectionFilterCombo.SelectedItem is SectionFilterOption selected
            ? selected.Raw
            : null;
        var entries = document.Entries
            .Where(entry => selectedSection is null || entry.Section.Equals(selectedSection, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            EntryList.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Config.NoEntries"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                Margin = new Thickness(4, 12)
            });
            return;
        }

        foreach (var entry in entries)
            EntryList.Children.Add(CreateEntryEditor(document, entry));
    }

    private Control CreateEntryEditor(ModConfigurationDocument document, ConfigurationEntry entry)
    {
        var developerMode = App.Services.Settings.DeveloperMode;
        var localized = App.Services.ConfigLocalization.Resolve(document, entry, App.Services.Localization.CurrentLanguage);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var titleStack = new StackPanel { Spacing = 2 };
        titleStack.Children.Add(new TextBlock
        {
            Text = localized.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (developerMode && localized.HasLocalization && !localized.Title.Equals(entry.Key, StringComparison.Ordinal))
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = entry.Key,
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        header.Children.Add(titleStack);

        var headerBadges = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (localized.Advanced)
            headerBadges.Children.Add(CreateBadge(App.Services.Localization.Get("Config.Advanced"), "Brush.Warning"));
        if (entry.IsDirty)
            headerBadges.Children.Add(CreateBadge(App.Services.Localization.Get("Config.Modified"), "Brush.Primary"));
        if (developerMode)
            headerBadges.Children.Add(CreateBadge(entry.TypeName, "Brush.TextSecondary"));
        Grid.SetColumn(headerBadges, 1);
        header.Children.Add(headerBadges);

        var stack = new StackPanel { Spacing = 7 };
        stack.Children.Add(header);

        var description = localized.Description;
        if (!string.IsNullOrWhiteSpace(description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (App.Services.Localization.CurrentLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) &&
            !localized.HasLocalization && !document.IsLoaderConfiguration)
        {
            stack.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Config.NotLocalized"),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            });
        }

        stack.Children.Add(CreateValueEditor(document, entry));

        if (!string.IsNullOrWhiteSpace(localized.Recommendation))
        {
            stack.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Config.Recommendation") + ": " + localized.Recommendation,
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (localized.RestartRequired)
        {
            stack.Children.Add(new TextBlock
            {
                Text = App.Services.Localization.Get("Config.RestartRequired"),
                Foreground = ResourceBrush("Brush.Warning"),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var meta = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var basicMeta = new List<string>();
        if (entry.DefaultValue is not null)
            basicMeta.Add(App.Services.Localization.Get("Config.Default") + ": " + entry.DefaultValue);
        if (entry.Minimum is not null || entry.Maximum is not null)
            basicMeta.Add($"{App.Services.Localization.Get("Config.Range")}: {entry.Minimum?.ToString() ?? "—"} – {entry.Maximum?.ToString() ?? "—"}");
        if (basicMeta.Count > 0)
        {
            meta.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", basicMeta),
                Foreground = ResourceBrush("Brush.TextSecondary"),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        if (entry.DefaultValue is not null)
        {
            var reset = new Button
            {
                Content = App.Services.Localization.Get("Config.ResetDefault"),
                Padding = new Thickness(9, 5),
                IsEnabled = !App.Services.IsBusy
            };
            reset.Classes.Add("secondary");
            reset.Click += (_, _) =>
            {
                entry.Value = entry.DefaultValue ?? entry.Value;
                RenderEntries();
                UpdateDirtyState(document);
            };
            Grid.SetColumn(reset, 1);
            meta.Children.Add(reset);
        }
        stack.Children.Add(meta);

        if (developerMode)
            stack.Children.Add(CreateDeveloperDetails(document, entry));

        return new Border
        {
            Background = ResourceBrush(entry.IsDirty ? "Brush.Elevated" : "Brush.Surface"),
            BorderBrush = ResourceBrush(entry.IsDirty ? "Brush.Primary" : "Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = stack
        };
    }

    private Control CreateDeveloperDetails(ModConfigurationDocument document, ConfigurationEntry entry)
    {
        var details = new StackPanel { Spacing = 3 };
        details.Children.Add(CreateDeveloperLine("Config.RawSection", entry.Section));
        details.Children.Add(CreateDeveloperLine("Config.RawKey", entry.Key));
        details.Children.Add(CreateDeveloperLine("Config.ValueType", entry.TypeName));
        details.Children.Add(CreateDeveloperLine("Config.SourceFile", Path.GetFileName(document.FilePath)));
        if (!string.IsNullOrWhiteSpace(document.PluginGuid))
            details.Children.Add(CreateDeveloperLine("Config.PluginGuid", document.PluginGuid!));

        return new Border
        {
            Background = ResourceBrush("Brush.Elevated"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 7),
            Child = details
        };
    }

    private Control CreateDeveloperLine(string labelKey, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 8 };
        grid.Children.Add(new TextBlock
        {
            Text = App.Services.Localization.Get(labelKey),
            Foreground = ResourceBrush("Brush.TextSecondary"),
            FontSize = 9
        });
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
        return grid;
    }

    private Control CreateValueEditor(ModConfigurationDocument document, ConfigurationEntry entry)
    {
        if (entry.Kind == ConfigurationValueKind.Boolean)
        {
            var checkbox = new CheckBox
            {
                Content = bool.TryParse(entry.Value, out var value) && value
                    ? App.Services.Localization.Get("Common.Enabled")
                    : App.Services.Localization.Get("Common.Disabled"),
                IsChecked = bool.TryParse(entry.Value, out value) && value,
                IsEnabled = !App.Services.IsBusy
            };
            checkbox.Click += (_, _) =>
            {
                entry.Value = checkbox.IsChecked == true ? "true" : "false";
                checkbox.Content = App.Services.Localization.Get(checkbox.IsChecked == true ? "Common.Enabled" : "Common.Disabled");
                UpdateDirtyState(document);
            };
            return checkbox;
        }

        if (entry.Kind == ConfigurationValueKind.Choice && entry.AllowedValues.Count > 0)
        {
            var combo = new ComboBox
            {
                ItemsSource = entry.AllowedValues,
                SelectedItem = entry.AllowedValues.FirstOrDefault(value => value.Equals(entry.Value, StringComparison.OrdinalIgnoreCase)) ?? entry.Value,
                MinWidth = 220,
                IsEnabled = !App.Services.IsBusy
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string selected)
                {
                    entry.Value = selected;
                    UpdateDirtyState(document);
                }
            };
            return combo;
        }

        var textBox = new TextBox
        {
            Text = entry.Value,
            MinWidth = 220,
            IsEnabled = !App.Services.IsBusy
        };
        textBox.TextChanged += (_, _) =>
        {
            entry.Value = textBox.Text ?? "";
            UpdateDirtyState(document);
        };
        return textBox;
    }

    private void UpdateDirtyState(ModConfigurationDocument document)
    {
        var count = document.DirtyCount;
        DirtyText.Text = count == 0
            ? App.Services.Localization.Get("Config.NoChanges")
            : string.Format(App.Services.Localization.Get("Config.UnsavedChanges"), count);
        var gameRunning = App.Services.Launcher.IsRunning(App.Services.Environment);
        SaveButton.IsEnabled = count > 0 && !gameRunning && !App.Services.IsBusy;
        RevertButton.IsEnabled = count > 0 && !App.Services.IsBusy;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var document = SelectedDocument();
        if (document is null) return;
        App.Services.SaveConfiguration(document);
        _restoreArmed = false;
    }

    private void Revert_Click(object? sender, RoutedEventArgs e)
    {
        var document = SelectedDocument();
        if (document is null) return;
        foreach (var entry in document.Entries)
            entry.Value = entry.OriginalValue;
        _restoreArmed = false;
        RenderDetails();
    }

    private void Restore_Click(object? sender, RoutedEventArgs e)
    {
        var document = SelectedDocument();
        if (document is null) return;
        if (!_restoreArmed)
        {
            _restoreArmed = true;
            RestoreButtonText.Text = App.Services.Localization.Get("Config.ConfirmRestore");
            return;
        }
        _restoreArmed = false;
        App.Services.RestoreLatestConfiguration(document);
    }

    private void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        var document = SelectedDocument();
        if (document is not null) App.Services.OpenConfigurationFile(document);
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var document = SelectedDocument();
        if (document is not null) App.Services.OpenConfigurationFolder(document);
    }

    private ModConfigurationDocument? SelectedDocument()
        => _selectedId is null ? null : App.Services.Configurations.FirstOrDefault(document =>
            document.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase));

    private void RefreshLoaderFilter()
    {
        if (LoaderFilterCombo is null) return;
        var selected = LoaderFilterCombo.SelectedIndex < 0 ? 0 : LoaderFilterCombo.SelectedIndex;
        LoaderFilterCombo.ItemsSource = new[]
        {
            App.Services.Localization.Get("Config.FilterAll"),
            "BepInEx",
            "MelonLoader",
            App.Services.Localization.Get("Config.FilterLoaders")
        };
        LoaderFilterCombo.SelectedIndex = Math.Min(selected, 3);
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
                Foreground = ResourceBrush(foregroundKey)
            }
        };
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

    private IBrush? ResourceBrush(string key) => this.FindResource(key) as IBrush;

    private sealed record SectionFilterOption(string Raw, string Display)
    {
        public override string ToString() => Display;
    }
}
