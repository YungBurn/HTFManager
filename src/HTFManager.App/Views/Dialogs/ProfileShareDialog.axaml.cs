using Avalonia.Controls;
using Avalonia.Interactivity;
using HTFManager.Core.Models;

namespace HTFManager.App.Views.Dialogs;

public partial class ProfileShareDialog : Window
{
    private ProfileBundleExportPlan _plan = new();

    public ProfileShareDialog()
    {
        InitializeComponent();
        Opened += (_, _) => Render();
        App.Services.Localization.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => App.Services.Localization.LanguageChanged -= OnLanguageChanged;
    }

    public ProfileShareDialog(ModProfile profile, ProfileBundleExportPlan plan) : this()
    {
        ProfileNameText.Text = profile.Name;
        _plan = plan;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        BundledCountText.Text = _plan.BundledCount.ToString();
        RemoteCountText.Text = _plan.RemoteOnlyCount.ToString();
        ManualCountText.Text = _plan.ManualCount.ToString();
        DriftCountText.Text = _plan.VersionDriftCount.ToString();
        EstimatedSizeText.Text = string.Format(
            App.Services.Localization.Get("Share.EstimatedSize"),
            FormatSize(_plan.EstimatedPayloadBytes));
    }

    private void Continue_Click(object? sender, RoutedEventArgs e)
        => Close(FullRadio.IsChecked == true ? "full" : "lightweight");

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close((string?)null);

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / 1024d / 1024d / 1024d:0.##} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / 1024d / 1024d:0.##} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:0.##} KB";
        return $"{bytes} B";
    }
}
