using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Configuration;

public sealed class ConfigurationService : IConfigurationService
{
    private static readonly Regex PluginHeaderRegex = new(
        @"^#+\s*Settings file was created by plugin\s+(?<name>.+?)\s+v(?<version>[^\s]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PluginGuidRegex = new(
        @"^#+\s*Plugin GUID:\s*(?<guid>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RangeRegex = new(
        @"From\s+(?<min>[-+0-9.,Ee]+)\s+to\s+(?<max>[-+0-9.,Ee]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _backupRoot;

    public ConfigurationService(string dataDirectory)
    {
        _backupRoot = Path.Combine(dataDirectory, "config-backups");
    }

    public IReadOnlyList<ModConfigurationDocument> Scan(GameEnvironmentInfo environment, IReadOnlyList<InstalledMod> mods)
    {
        var documents = new List<ModConfigurationDocument>();

        if (!string.IsNullOrWhiteSpace(environment.BepInEx.ConfigDirectory) &&
            Directory.Exists(environment.BepInEx.ConfigDirectory))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(environment.BepInEx.ConfigDirectory, "*.cfg", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                var isLoader = Path.GetFileName(file).Equals("BepInEx.cfg", StringComparison.OrdinalIgnoreCase);
                var document = Parse(file, ModLoaderKind.BepInEx, isLoader,
                    isLoader ? "BepInEx" : Path.GetFileNameWithoutExtension(file));
                if (document is not null)
                    documents.Add(document);
            }
        }

        var melonLoaderConfig = environment.MelonLoader.LoaderConfigPath;
        if (!string.IsNullOrWhiteSpace(melonLoaderConfig) && File.Exists(melonLoaderConfig))
        {
            var document = Parse(melonLoaderConfig, ModLoaderKind.MelonLoader, true, "MelonLoader");
            if (document is not null)
                documents.Add(document);
        }

        AssociateWithMods(documents, mods);
        return documents
            .OrderBy(document => document.IsLoaderConfiguration ? 0 : 1)
            .ThenBy(document => document.Loader)
            .ThenBy(document => document.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public ConfigurationOperationResult Save(ModConfigurationDocument document, bool createBackup, int maxBackups)
    {
        if (!File.Exists(document.FilePath))
            return ConfigurationOperationResult.Fail("Configuration file no longer exists.");

        var dirty = document.Entries.Where(entry => entry.IsDirty).ToArray();
        if (dirty.Length == 0)
            return ConfigurationOperationResult.Ok("No changes to save.");

        foreach (var entry in dirty)
        {
            var validation = Validate(entry);
            if (validation is not null)
                return ConfigurationOperationResult.Fail(validation);
        }

        string? backupPath = null;
        var temp = document.FilePath + ".htf.tmp";
        try
        {
            if (createBackup)
                backupPath = CreateBackup(document.FilePath, Math.Max(1, maxBackups));
            var lines = File.ReadAllLines(document.FilePath);
            var replacements = dirty.ToDictionary(
                entry => EntryKey(entry.Section, entry.Key),
                entry => entry,
                StringComparer.OrdinalIgnoreCase);

            var section = "General";
            var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (TryReadSection(trimmed, out var nextSection))
                {
                    section = nextSection;
                    continue;
                }

                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                    continue;

                var equals = lines[i].IndexOf('=');
                if (equals <= 0) continue;
                var key = lines[i][..equals].Trim();
                var lookup = EntryKey(section, key);
                if (!replacements.TryGetValue(lookup, out var entry)) continue;

                var afterEquals = lines[i][(equals + 1)..];
                var whitespaceLength = afterEquals.Length - afterEquals.TrimStart().Length;
                var whitespace = whitespaceLength > 0 ? afterEquals[..whitespaceLength] : " ";
                lines[i] = lines[i][..(equals + 1)] + whitespace + FormatValue(entry);
                replaced.Add(lookup);
            }

            if (replaced.Count != replacements.Count)
                return ConfigurationOperationResult.Fail("One or more settings could not be located in the current configuration file.");

            File.WriteAllLines(temp, lines, new global::System.Text.UTF8Encoding(false));
            File.Move(temp, document.FilePath, true);

            foreach (var entry in dirty)
                entry.OriginalValue = entry.Value;

            return ConfigurationOperationResult.Ok("Configuration saved.", backupPath);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return ConfigurationOperationResult.Fail(ex.Message);
        }
    }

    public IReadOnlyList<ConfigurationBackupInfo> GetBackups(ModConfigurationDocument document)
    {
        var directory = BackupDirectory(document.FilePath);
        if (!Directory.Exists(directory)) return Array.Empty<ConfigurationBackupInfo>();
        try
        {
            return Directory.EnumerateFiles(directory, "*.cfg", SearchOption.TopDirectoryOnly)
                .Select(path => new ConfigurationBackupInfo
                {
                    FilePath = path,
                    CreatedUtc = File.GetCreationTimeUtc(path)
                })
                .OrderByDescending(item => item.CreatedUtc)
                .ToArray();
        }
        catch
        {
            return Array.Empty<ConfigurationBackupInfo>();
        }
    }

    public ConfigurationOperationResult RestoreLatest(ModConfigurationDocument document, int maxBackups)
    {
        if (!File.Exists(document.FilePath))
            return ConfigurationOperationResult.Fail("Configuration file no longer exists.");

        var latest = GetBackups(document).FirstOrDefault();
        if (latest is null || !File.Exists(latest.FilePath))
            return ConfigurationOperationResult.Fail("No configuration backup is available.");

        var temp = document.FilePath + ".htf.restore.tmp";
        try
        {
            CreateBackup(document.FilePath, Math.Max(2, maxBackups));
            File.Copy(latest.FilePath, temp, true);
            File.Move(temp, document.FilePath, true);
            return ConfigurationOperationResult.Ok("Configuration restored.", latest.FilePath);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return ConfigurationOperationResult.Fail(ex.Message);
        }
    }

    private static ModConfigurationDocument? Parse(
        string path,
        ModLoaderKind loader,
        bool isLoaderConfiguration,
        string fallbackName)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return null; }

        var displayName = fallbackName;
        string? detectedVersion = null;
        string? pluginGuid = null;

        foreach (var raw in lines.Take(16))
        {
            var trimmed = raw.Trim();
            var header = PluginHeaderRegex.Match(trimmed);
            if (header.Success)
            {
                displayName = header.Groups["name"].Value.Trim();
                detectedVersion = header.Groups["version"].Value.Trim();
            }
            var guid = PluginGuidRegex.Match(trimmed);
            if (guid.Success)
                pluginGuid = guid.Groups["guid"].Value.Trim();
        }

        var entries = new List<ConfigurationEntry>();
        var section = "General";
        var descriptionLines = new List<string>();
        string? typeName = null;
        string? defaultValue = null;
        var allowedValues = new List<string>();
        double? minimum = null;
        double? maximum = null;

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (TryReadSection(trimmed, out var nextSection))
            {
                section = nextSection;
                ResetMetadata(descriptionLines, ref typeName, ref defaultValue, allowedValues, ref minimum, ref maximum);
                continue;
            }

            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                var comment = trimmed.TrimStart('#', ';').Trim();
                if (comment.StartsWith("Setting type:", StringComparison.OrdinalIgnoreCase))
                {
                    typeName = comment["Setting type:".Length..].Trim();
                }
                else if (comment.StartsWith("Default value:", StringComparison.OrdinalIgnoreCase))
                {
                    defaultValue = Unquote(comment["Default value:".Length..].Trim());
                }
                else if (comment.StartsWith("Acceptable values:", StringComparison.OrdinalIgnoreCase))
                {
                    allowedValues.Clear();
                    allowedValues.AddRange(comment["Acceptable values:".Length..]
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(Unquote));
                }
                else if (comment.StartsWith("Acceptable value range:", StringComparison.OrdinalIgnoreCase))
                {
                    var range = RangeRegex.Match(comment);
                    if (range.Success)
                    {
                        if (TryDouble(range.Groups["min"].Value, out var min)) minimum = min;
                        if (TryDouble(range.Groups["max"].Value, out var max)) maximum = max;
                    }
                }
                else if (!comment.StartsWith("Settings file was created", StringComparison.OrdinalIgnoreCase) &&
                         !comment.StartsWith("Plugin GUID:", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(comment))
                {
                    descriptionLines.Add(comment);
                }
                continue;
            }

            if (trimmed.Length == 0)
            {
                if (descriptionLines.Count > 0 && typeName is null && defaultValue is null)
                    descriptionLines.Clear();
                continue;
            }

            var equals = raw.IndexOf('=');
            if (equals <= 0) continue;
            var key = raw[..equals].Trim();
            if (key.Length == 0) continue;
            var rawValue = raw[(equals + 1)..].Trim();
            var quoteValue = rawValue.Length >= 2 && rawValue.StartsWith('"') && rawValue.EndsWith('"');
            var value = Unquote(rawValue);
            if (loader == ModLoaderKind.MelonLoader && isLoaderConfiguration)
            {
                var known = KnownMelonSetting(section, key);
                if (defaultValue is null && known.DefaultValue is not null)
                    defaultValue = known.DefaultValue;
                if (allowedValues.Count == 0 && known.AllowedValues.Count > 0)
                    allowedValues.AddRange(known.AllowedValues);
            }
            var kind = DetectKind(typeName, value, allowedValues);

            entries.Add(new ConfigurationEntry
            {
                Section = section,
                Key = key,
                Description = string.Join(" ", descriptionLines),
                TypeName = typeName ?? InferredTypeName(kind),
                Kind = kind,
                Value = value,
                OriginalValue = value,
                DefaultValue = defaultValue,
                AllowedValues = allowedValues.ToArray(),
                Minimum = minimum,
                Maximum = maximum,
                QuoteValue = quoteValue
            });

            ResetMetadata(descriptionLines, ref typeName, ref defaultValue, allowedValues, ref minimum, ref maximum);
        }

        return new ModConfigurationDocument
        {
            Id = loader + ":" + Path.GetFullPath(path).ToLowerInvariant(),
            DisplayName = displayName,
            FilePath = path,
            Loader = loader,
            IsLoaderConfiguration = isLoaderConfiguration,
            PluginGuid = pluginGuid,
            DetectedVersion = detectedVersion,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
            Entries = entries
        };
    }

    private static void AssociateWithMods(IEnumerable<ModConfigurationDocument> documents, IReadOnlyList<InstalledMod> mods)
    {
        foreach (var document in documents.Where(document => !document.IsLoaderConfiguration))
        {
            var fileToken = NormalizeToken(Path.GetFileNameWithoutExtension(document.FilePath));
            var displayToken = NormalizeToken(document.DisplayName);
            var guidToken = NormalizeToken(document.PluginGuid ?? "");

            var match = mods
                .Where(mod => mod.Loader == document.Loader)
                .Select(mod => new
                {
                    Mod = mod,
                    Score = AssociationScore(mod, fileToken, displayToken, guidToken)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();

            if (match is not null)
                document.AssociatedModId = match.Mod.Id;
        }
    }

    private static int AssociationScore(InstalledMod mod, string fileToken, string displayToken, string guidToken)
    {
        var name = NormalizeToken(mod.Name);
        var assembly = NormalizeToken(Path.GetFileNameWithoutExtension(mod.FilePath)
            .Replace(".dll", "", StringComparison.OrdinalIgnoreCase));
        var package = NormalizeToken(mod.PackageKey ?? "");
        var id = NormalizeToken(mod.Id);

        if (name.Length > 2 && name == displayToken) return 100;
        if (assembly.Length > 2 && assembly == displayToken) return 95;
        if (name.Length > 2 && name == fileToken) return 90;
        if (assembly.Length > 2 && assembly == fileToken) return 85;
        if (guidToken.Length > 3 && (id.Contains(guidToken, StringComparison.Ordinal) || package.Contains(guidToken, StringComparison.Ordinal))) return 80;
        if (displayToken.Length > 4 && name.Contains(displayToken, StringComparison.Ordinal)) return 60;
        if (name.Length > 4 && displayToken.Contains(name, StringComparison.Ordinal)) return 55;
        return 0;
    }

    private static string? Validate(ConfigurationEntry entry)
    {
        switch (entry.Kind)
        {
            case ConfigurationValueKind.Boolean:
                if (!bool.TryParse(entry.Value, out _))
                    return $"{entry.Section} / {entry.Key}: expected true or false.";
                break;
            case ConfigurationValueKind.Integer:
                if (!long.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    return $"{entry.Section} / {entry.Key}: expected an integer.";
                if (entry.Minimum is not null && integer < entry.Minimum.Value)
                    return $"{entry.Section} / {entry.Key}: value is below the allowed minimum.";
                if (entry.Maximum is not null && integer > entry.Maximum.Value)
                    return $"{entry.Section} / {entry.Key}: value is above the allowed maximum.";
                break;
            case ConfigurationValueKind.FloatingPoint:
                if (!TryDouble(entry.Value, out var number))
                    return $"{entry.Section} / {entry.Key}: expected a number.";
                if (entry.Minimum is not null && number < entry.Minimum.Value)
                    return $"{entry.Section} / {entry.Key}: value is below the allowed minimum.";
                if (entry.Maximum is not null && number > entry.Maximum.Value)
                    return $"{entry.Section} / {entry.Key}: value is above the allowed maximum.";
                break;
            case ConfigurationValueKind.Choice:
                if (entry.AllowedValues.Count > 0 && !entry.AllowedValues.Contains(entry.Value, StringComparer.OrdinalIgnoreCase))
                    return $"{entry.Section} / {entry.Key}: value is not in the allowed list.";
                break;
        }
        return null;
    }

    private string CreateBackup(string path, int maxBackups)
    {
        var directory = BackupDirectory(path);
        Directory.CreateDirectory(directory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var backup = Path.Combine(directory, stamp + ".cfg");
        File.Copy(path, backup, true);

        var backups = Directory.EnumerateFiles(directory, "*.cfg", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetCreationTimeUtc)
            .ToArray();
        foreach (var old in backups.Skip(Math.Max(1, maxBackups)))
        {
            try { File.Delete(old); } catch { }
        }
        return backup;
    }

    private string BackupDirectory(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path))))[..16];
        var stem = Sanitize(Path.GetFileNameWithoutExtension(path));
        return Path.Combine(_backupRoot, stem + "-" + hash);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }


    private static (string? DefaultValue, IReadOnlyList<string> AllowedValues) KnownMelonSetting(string section, string key)
    {
        var id = section.Trim().ToLowerInvariant() + "/" + key.Trim().ToLowerInvariant();
        return id switch
        {
            "loader/disable" => ("false", Array.Empty<string>()),
            "loader/debug_mode" => ("true", Array.Empty<string>()),
            "loader/capture_player_logs" => ("true", Array.Empty<string>()),
            "loader/harmony_log_level" => ("Warn", new[] { "None", "Error", "Warn", "Info", "Debug", "IL" }),
            "loader/force_quit" => ("false", Array.Empty<string>()),
            "loader/disable_start_screen" => ("false", Array.Empty<string>()),
            "loader/launch_debugger" => ("false", Array.Empty<string>()),
            "loader/theme" => ("Normal", new[] { "Normal", "Lemon" }),
            "console/hide_warnings" => ("false", Array.Empty<string>()),
            "console/hide_console" => ("false", Array.Empty<string>()),
            "console/console_on_top" => ("false", Array.Empty<string>()),
            "console/dont_set_title" => ("false", Array.Empty<string>()),
            "logs/max_logs" => ("10", Array.Empty<string>()),
            "mono_debug_server/debug_suspend" => ("false", Array.Empty<string>()),
            "mono_debug_server/debug_ip_address" => ("127.0.0.1", Array.Empty<string>()),
            "mono_debug_server/debug_port" => ("55555", Array.Empty<string>()),
            "unityengine/version_override" => ("", Array.Empty<string>()),
            "unityengine/disable_console_log_cleaner" => ("false", Array.Empty<string>()),
            "unityengine/force_offline_generation" => ("false", Array.Empty<string>()),
            "unityengine/force_generator_regex" => ("", Array.Empty<string>()),
            "unityengine/force_il2cpp_dumper_version" => ("", Array.Empty<string>()),
            "unityengine/force_regeneration" => ("false", Array.Empty<string>()),
            "unityengine/enable_cpp2il_call_analyzer" => ("false", Array.Empty<string>()),
            "unityengine/enable_cpp2il_native_method_detector" => ("false", Array.Empty<string>()),
            _ => (null, Array.Empty<string>())
        };
    }

    private static ConfigurationValueKind DetectKind(string? typeName, string value, IReadOnlyCollection<string> allowedValues)
    {
        if (allowedValues.Count > 0) return ConfigurationValueKind.Choice;
        var type = typeName?.Trim() ?? "";
        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) || bool.TryParse(value, out _))
            return ConfigurationValueKind.Boolean;
        if (type.Contains("Int", StringComparison.OrdinalIgnoreCase) || type.Contains("Byte", StringComparison.OrdinalIgnoreCase) ||
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return ConfigurationValueKind.Integer;
        if (type.Equals("Single", StringComparison.OrdinalIgnoreCase) || type.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Decimal", StringComparison.OrdinalIgnoreCase) || TryDouble(value, out _))
            return ConfigurationValueKind.FloatingPoint;
        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("String", StringComparison.OrdinalIgnoreCase))
            return ConfigurationValueKind.Choice;
        return ConfigurationValueKind.Text;
    }

    private static string InferredTypeName(ConfigurationValueKind kind) => kind switch
    {
        ConfigurationValueKind.Boolean => "Boolean",
        ConfigurationValueKind.Integer => "Integer",
        ConfigurationValueKind.FloatingPoint => "Number",
        ConfigurationValueKind.Choice => "Choice",
        _ => "String"
    };

    private static string FormatValue(ConfigurationEntry entry)
    {
        if (entry.Kind == ConfigurationValueKind.Boolean && bool.TryParse(entry.Value, out var boolean))
        {
            var originalLower = entry.OriginalValue.Equals(entry.OriginalValue.ToLowerInvariant(), StringComparison.Ordinal);
            var value = boolean ? "true" : "false";
            return originalLower ? value : char.ToUpperInvariant(value[0]) + value[1..];
        }

        var valueText = entry.Value;
        if (entry.Kind == ConfigurationValueKind.FloatingPoint && TryDouble(entry.Value, out var number))
            valueText = number.ToString("R", CultureInfo.InvariantCulture);
        if (!entry.QuoteValue) return valueText;
        var escaped = valueText.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }
        return trimmed;
    }

    private static bool TryReadSection(string trimmed, out string section)
    {
        if (trimmed.Length >= 3 && trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            section = trimmed[1..^1].Trim();
            return section.Length > 0;
        }
        section = "";
        return false;
    }

    private static void ResetMetadata(
        List<string> descriptionLines,
        ref string? typeName,
        ref string? defaultValue,
        List<string> allowedValues,
        ref double? minimum,
        ref double? maximum)
    {
        descriptionLines.Clear();
        typeName = null;
        defaultValue = null;
        allowedValues.Clear();
        minimum = null;
        maximum = null;
    }

    private static bool TryDouble(string value, out double result)
        => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result) ||
           double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result);

    private static string EntryKey(string section, string key) => section.Trim() + "\u001F" + key.Trim();

    private static string NormalizeToken(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
