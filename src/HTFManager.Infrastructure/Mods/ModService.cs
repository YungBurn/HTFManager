using System.Reflection;
using System.Text.Json;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Infrastructure.Mods;

public sealed class ModService : IModService
{
    private readonly ModRegistryStore _registry;

    public ModService(ModRegistryStore registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<InstalledMod> Scan(GameEnvironmentInfo environment)
    {
        if (environment.GameDirectory is null || !Directory.Exists(environment.GameDirectory))
            return Array.Empty<InstalledMod>();

        var gameDirectory = environment.GameDirectory;
        var mods = new List<InstalledMod>();
        var managedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in _registry.LoadAll().Where(r => SamePath(r.GameDirectory, gameDirectory)))
        {
            foreach (var relative in record.Files)
                managedFiles.Add(NormalizeRelative(relative));

            mods.Add(CreateManagedMod(gameDirectory, record));
        }

        ScanExternalRoot(mods, managedFiles, gameDirectory, environment.BepInEx.PluginsDirectory,
            ModLoaderKind.BepInEx, ModComponentKind.Plugin, "external:", ModPackageKind.BepInExPlugin);

        ScanExternalRoot(mods, managedFiles, gameDirectory, environment.BepInEx.PatchersDirectory,
            ModLoaderKind.BepInEx, ModComponentKind.Patcher, "external:patcher:", ModPackageKind.BepInExPackage);

        ScanExternalRoot(mods, managedFiles, gameDirectory, environment.MelonLoader.ModsDirectory,
            ModLoaderKind.MelonLoader, ModComponentKind.Mod, "external:melon-mod:", ModPackageKind.Unknown);

        ScanExternalRoot(mods, managedFiles, gameDirectory, environment.MelonLoader.PluginsDirectory,
            ModLoaderKind.MelonLoader, ModComponentKind.Plugin, "external:melon-plugin:", ModPackageKind.Unknown);

        return mods
            .OrderBy(m => m.Loader)
            .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool SetEnabled(InstalledMod mod, bool enabled)
    {
        try
        {
            if (mod.IsManaged && mod.OwnedFiles.Count > 0)
            {
                var loadableDlls = mod.OwnedFiles.Where(IsManagedLoadableDll).ToArray();
                if (loadableDlls.Length == 0) return true;

                foreach (var canonical in loadableDlls)
                {
                    var disabled = canonical + ".disabled";
                    if (enabled && File.Exists(disabled) && File.Exists(canonical)) return false;
                    if (!enabled && File.Exists(canonical) && File.Exists(disabled)) return false;
                }

                foreach (var canonical in loadableDlls)
                {
                    var disabled = canonical + ".disabled";
                    if (enabled && File.Exists(disabled) && !File.Exists(canonical))
                        File.Move(disabled, canonical);
                    else if (!enabled && File.Exists(canonical) && !File.Exists(disabled))
                        File.Move(canonical, disabled);
                }

                return true;
            }

            var source = mod.FilePath;
            var currentlyEnabled = source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                                   !source.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
            if (currentlyEnabled == enabled)
                return true;

            if (enabled && !source.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                return false;

            var target = enabled
                ? source[..^".disabled".Length]
                : source + ".disabled";

            if (File.Exists(target))
                return false;

            File.Move(source, target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ScanExternalRoot(
        ICollection<InstalledMod> destination,
        ISet<string> managedFiles,
        string gameDirectory,
        string? root,
        ModLoaderKind loader,
        ModComponentKind component,
        string idPrefix,
        ModPackageKind kind)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsPluginFile)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            var canonical = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? file[..^".disabled".Length]
                : file;
            var relativeToGame = NormalizeRelative(Path.GetRelativePath(gameDirectory, canonical));
            if (managedFiles.Contains(relativeToGame)) continue;

            destination.Add(CreateExternalMod(root, file, loader, component, idPrefix, kind));
        }
    }

    private static InstalledMod CreateManagedMod(string gameDirectory, ModInstallationRecord record)
    {
        var loader = record.Loader != ModLoaderKind.Unknown ? record.Loader : InferLoader(record.Files);
        var component = record.Component != ModComponentKind.Unknown ? record.Component : InferComponent(record.Files, loader);
        var loadableDlls = record.Files
            .Where(IsManagedLoadableDllRelative)
            .Select(relative => Path.Combine(gameDirectory, relative.Replace('/', Path.DirectorySeparatorChar)))
            .ToArray();

        var enabled = loadableDlls.Length == 0 || loadableDlls.Any(File.Exists);
        var primary = loadableDlls.FirstOrDefault(File.Exists)
                      ?? loadableDlls.Select(p => p + ".disabled").FirstOrDefault(File.Exists)
                      ?? DefaultRoot(gameDirectory, loader, component);

        return new InstalledMod
        {
            Id = "managed:" + record.Id,
            RegistryId = record.Id,
            Name = record.Name,
            FilePath = primary,
            Version = record.Version,
            Author = record.Author,
            Description = record.Description,
            Enabled = enabled,
            IsExternal = false,
            IsManaged = true,
            PackageKey = record.PackageKey,
            Source = record.Source,
            Kind = record.Kind,
            Loader = loader,
            Component = component,
            OwnedFiles = record.Files
                .Select(relative => Path.Combine(gameDirectory, relative.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray()
        };
    }

    private static InstalledMod CreateExternalMod(
        string root,
        string file,
        ModLoaderKind loader,
        ModComponentKind component,
        string idPrefix,
        ModPackageKind kind)
    {
        var enabled = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                      !file.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
        var relative = Path.GetRelativePath(root, file);
        if (relative.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase))
            relative = relative[..^".disabled".Length];
        var displayName = Path.GetFileName(file)
            .Replace(".dll.disabled", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".dll", "", StringComparison.OrdinalIgnoreCase);
        var version = "—";

        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(file);
            displayName = assemblyName.Name ?? displayName;
            version = assemblyName.Version?.ToString() ?? "—";
        }
        catch { }

        var (manifestName, manifestVersion, description) = ReadManifest(file);
        if (!string.IsNullOrWhiteSpace(manifestName)) displayName = manifestName!;
        if (!string.IsNullOrWhiteSpace(manifestVersion)) version = manifestVersion!;

        return new InstalledMod
        {
            Id = idPrefix + relative.Replace('\\', '/'),
            Name = displayName,
            FilePath = file,
            Version = version,
            Description = description ?? "",
            Enabled = enabled,
            IsExternal = true,
            IsManaged = false,
            Source = ModSourceType.External,
            Kind = kind,
            Loader = loader,
            Component = component
        };
    }

    private static (string? Name, string? Version, string? Description) ReadManifest(string dllPath)
    {
        foreach (var directory in CandidateDirectories(dllPath))
        {
            var manifest = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifest)) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;
                string? Get(string key) => root.TryGetProperty(key, out var value) ? value.GetString() : null;
                return (Get("name"), Get("version_number"), Get("description"));
            }
            catch { }
        }

        return (null, null, null);
    }

    private static IEnumerable<string> CandidateDirectories(string file)
    {
        var dir = Path.GetDirectoryName(file);
        if (dir is null) yield break;
        yield return dir;
        var parent = Directory.GetParent(dir)?.FullName;
        if (parent is not null) yield return parent;
    }

    private static ModLoaderKind InferLoader(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var normalized = NormalizeRelative(file);
            if (normalized.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) return ModLoaderKind.BepInEx;
            if (normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase)) return ModLoaderKind.MelonLoader;
        }
        return ModLoaderKind.Unknown;
    }

    private static ModComponentKind InferComponent(IEnumerable<string> files, ModLoaderKind loader)
    {
        foreach (var file in files)
        {
            var normalized = NormalizeRelative(file);
            if (normalized.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase)) return ModComponentKind.Patcher;
            if (normalized.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)) return ModComponentKind.Content;
            if (normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)) return ModComponentKind.Mod;
            if (normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase)) return ModComponentKind.Plugin;
            if (normalized.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)) return ModComponentKind.Plugin;
        }
        return loader == ModLoaderKind.MelonLoader ? ModComponentKind.Mod : ModComponentKind.Unknown;
    }

    private static string DefaultRoot(string gameDirectory, ModLoaderKind loader, ModComponentKind component)
    {
        if (loader == ModLoaderKind.MelonLoader)
            return Path.Combine(gameDirectory, component == ModComponentKind.Plugin ? "Plugins" : "Mods");
        return Path.Combine(gameDirectory, "BepInEx", component == ModComponentKind.Patcher ? "patchers" : "plugins");
    }

    private static bool IsPluginFile(string path)
        => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);

    private static bool IsManagedLoadableDll(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/');
        return (normalized.Contains("/BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/BepInEx/patchers/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Mods/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Plugins/", StringComparison.OrdinalIgnoreCase)) &&
               fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManagedLoadableDllRelative(string relative)
    {
        var normalized = NormalizeRelative(relative);
        return (normalized.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase)) &&
               normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }
}
