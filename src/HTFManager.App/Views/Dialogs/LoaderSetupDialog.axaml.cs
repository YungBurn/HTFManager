using Avalonia.Controls;
using Avalonia.Interactivity;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class LoaderSetupDialog : Window
{
    private readonly ModLoaderKind _loader;
    private bool _uninstallArmed;

    public LoaderSetupDialog(ModLoaderKind loader)
    {
        _loader = loader;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        var recommendation = App.Services.GetLoaderRecommendation(_loader);
        var record = App.Services.GetManagedLoaderRecord(_loader);
        var environment = App.Services.Environment;
        var healthy = _loader == ModLoaderKind.BepInEx ? environment.BepInEx.Healthy : environment.MelonLoader.Healthy;
        var detected = _loader == ModLoaderKind.BepInEx ? environment.BepInEx.Installed : environment.MelonLoader.Detected;
        var installedVersion = _loader == ModLoaderKind.BepInEx ? environment.BepInEx.Version : environment.MelonLoader.Version;
        var otherHealthy = _loader == ModLoaderKind.BepInEx ? environment.MelonLoader.Healthy : environment.BepInEx.Healthy;

        LoaderNameText.Text = _loader == ModLoaderKind.BepInEx ? "BepInEx" : "MelonLoader";
        RecommendedVersionText.Text = recommendation.Version;
        InstalledVersionText.Text = installedVersion;
        SourceText.Text = recommendation.SourceName;
        GamePathText.Text = environment.GameDirectory ?? App.Services.Localization.Get("Common.NotFound");
        ManualLayoutText.Text = App.Services.Localization.Get(_loader == ModLoaderKind.BepInEx ? "LoaderSetup.ManualBep" : "LoaderSetup.ManualMelon");
        StatusText.Text = healthy
            ? (record is null ? App.Services.Localization.Get("LoaderSetup.ExternalInstall") : App.Services.Localization.Get("Common.Ready"))
            : detected ? App.Services.Localization.Get("Common.NeedsAttention") : App.Services.Localization.Get("LoaderSetup.NotInstalled");

        MixedWarningPanel.IsVisible = otherHealthy && !healthy;
        PrimaryButtonText.Text = App.Services.Localization.Get(healthy ? "LoaderSetup.RepairUpdate" : "LoaderSetup.AutoInstall");
        PrimaryButton.IsEnabled = !App.Services.IsBusy && (!detected || record is not null);
        UninstallButton.IsVisible = record is not null;
        UninstallButtonText.Text = App.Services.Localization.Get(_uninstallArmed ? "LoaderSetup.ConfirmUninstall" : "LoaderSetup.Uninstall");
        RenderValidation();
    }

    private void RenderValidation()
    {
        ValidationList.Children.Clear();
        foreach (var item in App.Services.ValidateLoader(_loader))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = item.Passed ? "✓" : "!" });
            var name = new TextBlock { Text = item.Name, FontSize = 11 };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var state = new TextBlock { Text = App.Services.Localization.Get(item.Passed ? "Tools.Passed" : "Tools.Failed"), FontSize = 11 };
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            ValidationList.Children.Add(row);
        }
    }

    private async void Primary_Click(object? sender, RoutedEventArgs e)
    {
        PrimaryButton.IsEnabled = false;
        await App.Services.InstallOrRepairLoaderAsync(_loader);
        _uninstallArmed = false;
        Render();
    }

    private void Validate_Click(object? sender, RoutedEventArgs e) => RenderValidation();
    private void OpenSource_Click(object? sender, RoutedEventArgs e) => App.Services.OpenLoaderSource(_loader);

    private void Uninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (!_uninstallArmed)
        {
            _uninstallArmed = true;
            Render();
            return;
        }
        App.Services.UninstallLoader(_loader);
        _uninstallArmed = false;
        Render();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(true);
}
