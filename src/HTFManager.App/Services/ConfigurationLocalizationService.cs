using System.Text.Json;
using Avalonia.Platform;
using HTFManager.Core.Models;

namespace HTFManager.App.Services;

public sealed class ConfigurationLocalizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, ConfigurationLocalizationSchema?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurationLocalizationResult Resolve(
        ModConfigurationDocument document,
        ConfigurationEntry entry,
        string language)
    {
        var raw = new ConfigurationLocalizationResult
        {
            Title = entry.Key,
            Description = entry.Description,
            SectionTitle = entry.Section,
            HasLocalization = false
        };

        if (!language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            return raw;

        var source = SourceId(document);
        if (source is null)
            return raw;

        var schema = Load(source, language);
        if (schema is null)
            return raw;

        var sectionTitle = schema.Sections.TryGetValue(entry.Section, out var localizedSection)
            ? localizedSection
            : entry.Section;
        var key = EntryId(entry.Section, entry.Key);
        if (!schema.Entries.TryGetValue(key, out var localized))
        {
            raw.SectionTitle = sectionTitle;
            return raw;
        }

        return new ConfigurationLocalizationResult
        {
            Title = string.IsNullOrWhiteSpace(localized.Title) ? entry.Key : localized.Title,
            Description = string.IsNullOrWhiteSpace(localized.Description) ? entry.Description : localized.Description,
            SectionTitle = sectionTitle,
            Recommendation = localized.Recommendation,
            Advanced = localized.Advanced,
            RestartRequired = localized.RestartRequired,
            HasLocalization = true
        };
    }

    public string GetSectionTitle(ModConfigurationDocument document, string section, string language)
    {
        if (!language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            return section;

        var source = SourceId(document);
        var schema = source is null ? null : Load(source, language);
        return schema is not null && schema.Sections.TryGetValue(section, out var localized)
            ? localized
            : section;
    }

    public bool HasKnownSchema(ModConfigurationDocument document, string language)
    {
        if (!language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            return false;
        var source = SourceId(document);
        return source is not null && Load(source, language) is not null;
    }

    private ConfigurationLocalizationSchema? Load(string source, string language)
    {
        var cacheKey = source + ":" + language;
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var uri = new Uri($"avares://HTFManager/Localization/ConfigSchemas/{source}/{language}.json");
            using var stream = AssetLoader.Open(uri);
            var schema = JsonSerializer.Deserialize<ConfigurationLocalizationSchema>(stream, JsonOptions);
            if (schema is not null)
            {
                schema.Sections = new Dictionary<string, string>(schema.Sections, StringComparer.OrdinalIgnoreCase);
                schema.Entries = new Dictionary<string, ConfigurationLocalizationEntry>(schema.Entries, StringComparer.OrdinalIgnoreCase);
            }
            _cache[cacheKey] = schema;
            return schema;
        }
        catch
        {
            _cache[cacheKey] = null;
            return null;
        }
    }

    private static string? SourceId(ModConfigurationDocument document)
    {
        if (!document.IsLoaderConfiguration)
            return null;

        return document.Loader switch
        {
            ModLoaderKind.BepInEx => "BepInEx",
            ModLoaderKind.MelonLoader => "MelonLoader",
            _ => null
        };
    }

    private static string EntryId(string section, string key) => section.Trim() + "/" + key.Trim();

    public sealed class ConfigurationLocalizationSchema
    {
        public Dictionary<string, string> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ConfigurationLocalizationEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ConfigurationLocalizationEntry
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Recommendation { get; set; }
        public bool Advanced { get; set; }
        public bool RestartRequired { get; set; } = true;
    }
}

public sealed class ConfigurationLocalizationResult
{
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public required string SectionTitle { get; set; }
    public string? Recommendation { get; init; }
    public bool Advanced { get; init; }
    public bool RestartRequired { get; init; }
    public bool HasLocalization { get; init; }
}
