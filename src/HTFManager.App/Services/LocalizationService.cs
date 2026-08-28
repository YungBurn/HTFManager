using System.Text.Json;
using Avalonia;
using Avalonia.Platform;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.App.Services;

public sealed class LocalizationService(ISettingsStore settingsStore, AppSettings settings)
{
    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguage => settings.Language;
    public event EventHandler? LanguageChanged;

    public void Initialize() => Apply(settings.Language, persist: false);

    public void SetLanguage(string language) => Apply(language, persist: true);

    public string Get(string key) => _strings.TryGetValue(key, out var value) ? value : key;

    private void Apply(string language, bool persist)
    {
        language = language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
        Dictionary<string, string>? data = null;
        try
        {
            var uri = new Uri($"avares://HTFManager/Localization/{language}.json");
            using var stream = AssetLoader.Open(uri);
            data = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }
        catch { }

        data ??= BuiltInFallback(language);
        _strings.Clear();

        foreach (var pair in data)
        {
            _strings[pair.Key] = pair.Value;
            if (Application.Current is not null)
                Application.Current.Resources[pair.Key] = pair.Value;
        }

        settings.Language = language;
        if (persist)
            settingsStore.Save(settings);

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, string> BuiltInFallback(string language)
    {
        if (language == "en-US")
        {
            return new Dictionary<string, string>
            {
                ["App.Name"] = "HTF Manager",
                ["Nav.Home"] = "Home",
                ["Nav.Mods"] = "Mods",
                ["Nav.Discover"] = "Discover",
                ["Nav.Profiles"] = "Profiles",
                ["Nav.Tools"] = "Tools",
                ["Nav.Settings"] = "Settings",
                ["Status.Ready"] = "Ready"
            };
        }

        return new Dictionary<string, string>
        {
            ["App.Name"] = "HTF Manager",
            ["Nav.Home"] = "首页",
            ["Nav.Mods"] = "模组",
            ["Nav.Discover"] = "发现",
            ["Nav.Profiles"] = "配置档",
            ["Nav.Tools"] = "工具",
            ["Nav.Settings"] = "设置",
            ["Status.Ready"] = "就绪"
        };
    }
}
