using Avalonia.Controls;

namespace HTFManager.App.Views.Home;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        App.Services.StateChanged += (_, _) => Refresh();
        App.Services.Localization.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var env = App.Services.Environment;
        var mods = App.Services.Mods;
        InstalledCountText.Text = mods.Count.ToString();
        EnabledCountText.Text = $"{mods.Count(m => m.Enabled)} {App.Services.Localization.Get("Home.EnabledMods")}";
        EnvironmentText.Text = env.IsHealthy
            ? App.Services.Localization.Get("Home.StatusHealthy")
            : App.Services.Localization.Get("Home.StatusNeedsAttention");
        var loaderParts = new List<string>();
        if (env.BepInEx.Installed) loaderParts.Add($"BepInEx {env.BepInEx.Version}");
        if (env.MelonLoader.Installed) loaderParts.Add($"MelonLoader {env.MelonLoader.Version}");
        BepVersionText.Text = loaderParts.Count > 0 ? string.Join("  ·  ", loaderParts) : App.Services.Localization.Get("Common.NotFound");
        ProfileText.Text = App.Services.Settings.ActiveProfile;
        GameVersionText.Text = $"{App.Services.Localization.Get("Common.Game")} {env.GameVersion}";
        GamePathText.Text = env.GameDirectory ?? App.Services.Localization.Get("Common.NotFound");

        if (env.IsHealthy)
        {
            HealthTitle.Text = App.Services.Localization.Get("Home.StatusHealthy");
            HealthDetail.Text = string.Join(" + ", loaderParts);
            HealthCheckIcon.IsVisible = true;
            HealthWarningIcon.IsVisible = false;
        }
        else
        {
            HealthTitle.Text = App.Services.Localization.Get("Home.StatusNeedsAttention");
            HealthDetail.Text = App.Services.Localization.Get("Status.GameMissing");
            HealthCheckIcon.IsVisible = false;
            HealthWarningIcon.IsVisible = true;
        }
    }
}
