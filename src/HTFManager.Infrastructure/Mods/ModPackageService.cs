using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;
using HTFManager.Infrastructure.Storage;

namespace HTFManager.Infrastructure.Mods;

public sealed class ModPackageService : IModPackageService
{
    private readonly ModRegistryStore _registry;
    private readonly string _dataDirectory;

    public ModPackageService(ModRegistryStore registry, string dataDirectory)
    {
        _registry = registry;
        _dataDirectory = dataDirectory;
    }

    public Task<PackageInspectionResult> InspectAsync(string sourcePath, CancellationToken cancellationToken = default)
        => Task.Run(() => BuildPlan(sourcePath, null).Inspection, cancellationToken);

    public Task<PackageInspectionResult> InspectForInstallAsync(
        string sourcePath,
        GameEnvironmentInfo environment,
        ModInstallMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var plan = BuildPlan(sourcePath, metadata);
            var inspection = plan.Inspection;
            if (!inspection.IsValid || environment.GameDirectory is null)
                return EnrichInspection(inspection, plan.Source, sourcePath, false, null, Array.Empty<string>(), false);

            var existing = FindExistingRecord(environment.GameDirectory, plan);
            var conflicts = FindConflicts(environment.GameDirectory, plan.Files, existing);
            var missingLoader = inspection.Loader switch
            {
                ModLoaderKind.BepInEx => !environment.BepInEx.Healthy,
                ModLoaderKind.MelonLoader => !environment.MelonLoader.Healthy,
                _ => false
            };

            return EnrichInspection(
                inspection,
                plan.Source,
                sourcePath,
                existing is not null,
                existing?.Version,
                conflicts,
                missingLoader);
        }, cancellationToken);
    }

    public Task<ModOperationResult> InstallAsync(
        string sourcePath,
        GameEnvironmentInfo environment,
        ModInstallMetadata? metadata = null,
        bool autoEnable = true,
        bool keepPackageCache = true,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => InstallCore(sourcePath, environment, metadata, autoEnable, keepPackageCache), cancellationToken);
    }

    public ModOperationResult Uninstall(InstalledMod mod, GameEnvironmentInfo environment, bool preserveConfig = true)
    {
        if (!mod.IsManaged || string.IsNullOrWhiteSpace(mod.RegistryId) || environment.GameDirectory is null)
            return ModOperationResult.Fail("This mod is not managed by HTF Manager.");

        var record = _registry.Find(mod.RegistryId);
        if (record is null)
            return ModOperationResult.Fail("The installation record no longer exists.");

        try
        {
            foreach (var relative in record.Files)
            {
                var canonical = SafeCombine(environment.GameDirectory, relative);
                DeleteIfExists(canonical);
                DeleteIfExists(canonical + ".disabled");
            }

            if (!preserveConfig)
            {
                foreach (var relative in record.UserDataFiles)
                {
                    var canonical = SafeCombine(environment.GameDirectory, relative);
                    DeleteIfExists(canonical);
                }
            }

            _registry.Delete(record.Id);
            PruneEmptyDirectories(environment.GameDirectory, record.Files.Concat(record.UserDataFiles));
            return ModOperationResult.Ok("Uninstalled.");
        }
        catch (Exception ex)
        {
            return ModOperationResult.Fail(ex.Message);
        }
    }

    private ModOperationResult InstallCore(
        string sourcePath,
        GameEnvironmentInfo environment,
        ModInstallMetadata? metadata,
        bool autoEnable,
        bool keepPackageCache)
    {
        if (environment.GameDirectory is null || !environment.GameFound)
            return ModOperationResult.Fail("Game directory is not available.");

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return ModOperationResult.Fail("Source file does not exist.");

        InstallPlan plan;
        try
        {
            plan = BuildPlan(sourcePath, metadata);
        }
        catch (Exception ex)
        {
            return ModOperationResult.Fail(ex.Message);
        }

        if (!plan.Inspection.IsValid)
            return ModOperationResult.Fail(plan.Inspection.Error ?? "Package validation failed.", plan.Inspection);

        if (plan.Inspection.Loader == ModLoaderKind.BepInEx && !environment.BepInEx.Healthy)
            return ModOperationResult.Fail("BepInEx is required for this package but is not ready.", plan.Inspection);
        if (plan.Inspection.Loader == ModLoaderKind.MelonLoader && !environment.MelonLoader.Healthy)
            return ModOperationResult.Fail("MelonLoader is required for this package but is not ready.", plan.Inspection);

        var gameDirectory = environment.GameDirectory;
        var existing = FindExistingRecord(gameDirectory, plan);
        var shouldEnable = existing is null ? autoEnable : IsRecordEnabled(existing, gameDirectory);

        if (plan.Files.Count == 0 && plan.Inspection.Kind == ModPackageKind.Modpack)
        {
            var virtualRecord = CreateRecord(plan, gameDirectory, sourcePath, existing, Array.Empty<string>(), Array.Empty<string>());
            _registry.Save(virtualRecord);
            CacheSource(sourcePath, virtualRecord, keepPackageCache);
            return ModOperationResult.Ok("Modpack registered.", virtualRecord, plan.Inspection);
        }

        var conflicts = FindConflicts(gameDirectory, plan.Files, existing);
        if (conflicts.Count > 0)
            return ModOperationResult.Fail("File conflict: " + string.Join(", ", conflicts.Take(3)), plan.Inspection);

        var transactionRoot = Path.Combine(_dataDirectory, "staging", Guid.NewGuid().ToString("N"));
        var stageRoot = Path.Combine(transactionRoot, "files");
        var backupRoot = Path.Combine(transactionRoot, "backup");
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installedTargets = new List<string>();

        try
        {
            Directory.CreateDirectory(stageRoot);
            Directory.CreateDirectory(backupRoot);
            StageFiles(sourcePath, plan, stageRoot);

            var allRelativeFiles = plan.Files.Select(x => NormalizeRelative(x.TargetRelative)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var newRelativeFiles = allRelativeFiles.Where(x => !IsConfigPath(x)).ToArray();
            var userDataFiles = allRelativeFiles.Where(IsConfigPath).ToArray();
            var obsolete = existing?.Files
                .Where(old => !newRelativeFiles.Contains(NormalizeRelative(old), StringComparer.OrdinalIgnoreCase))
                .Where(old => !IsConfigPath(old))
                .ToArray() ?? Array.Empty<string>();

            foreach (var file in plan.Files)
            {
                var relative = NormalizeRelative(file.TargetRelative);
                var canonical = SafeCombine(gameDirectory, relative);
                BackupExisting(canonical, backupRoot, backups);
                BackupExisting(canonical + ".disabled", backupRoot, backups);
            }

            foreach (var relative in obsolete)
            {
                var canonical = SafeCombine(gameDirectory, relative);
                BackupExisting(canonical, backupRoot, backups);
                BackupExisting(canonical + ".disabled", backupRoot, backups);
            }

            foreach (var file in plan.Files)
            {
                var relative = NormalizeRelative(file.TargetRelative);
                var staged = SafeCombine(stageRoot, relative);
                var target = SafeCombine(gameDirectory, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (IsConfigPath(relative) && File.Exists(target))
                    continue;

                DeleteIfExists(target);
                DeleteIfExists(target + ".disabled");
                File.Copy(staged, target, true);
                installedTargets.Add(target);

                if (!shouldEnable && IsLoadableDll(relative))
                {
                    File.Move(target, target + ".disabled", true);
                    installedTargets[^1] = target + ".disabled";
                }
            }

            foreach (var relative in obsolete)
            {
                var target = SafeCombine(gameDirectory, relative);
                DeleteIfExists(target);
                DeleteIfExists(target + ".disabled");
            }

            var record = CreateRecord(plan, gameDirectory, sourcePath, existing, newRelativeFiles, userDataFiles);
            _registry.Save(record);
            CacheSource(sourcePath, record, keepPackageCache);
            return ModOperationResult.Ok(existing is null ? "Installed." : "Updated.", record, plan.Inspection);
        }
        catch (Exception ex)
        {
            try
            {
                foreach (var path in installedTargets)
                    DeleteIfExists(path);

                foreach (var pair in backups)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);
                    File.Copy(pair.Value, pair.Key, true);
                }
            }
            catch
            {
                // Best-effort rollback. The original exception is more useful to the caller.
            }

            return ModOperationResult.Fail(ex.Message, plan.Inspection);
        }
        finally
        {
            try { if (Directory.Exists(transactionRoot)) Directory.Delete(transactionRoot, true); }
            catch { }
        }
    }

    private InstallPlan BuildPlan(string sourcePath, ModInstallMetadata? metadata)
    {
        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return BuildDllPlan(sourcePath, metadata);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return BuildZipPlan(sourcePath, metadata);

        return new InstallPlan
        {
            Inspection = new PackageInspectionResult
            {
                IsValid = false,
                Error = "Only .zip and .dll packages are supported in this version."
            }
        };
    }

    private static InstallPlan BuildDllPlan(string sourcePath, ModInstallMetadata? metadata)
    {
        var analysis = ManagedAssemblyInspector.Inspect(sourcePath);
        if (!analysis.IsManaged || analysis.Loader == ModLoaderKind.Unknown)
            return Invalid(analysis.Error ?? "The DLL was not recognized as a supported mod.");
        if (analysis.Loader == ModLoaderKind.MelonLoader && analysis.Component == ModComponentKind.Unknown)
            return Invalid("The DLL contains both MelonMod and MelonPlugin entry types and cannot be placed safely as a single file.");

        var name = metadata?.Name ?? analysis.AssemblyName;
        var version = metadata?.Version ?? analysis.Version;
        var author = metadata?.Author ?? "Local";
        var fileName = Path.GetFileName(sourcePath);
        var folder = SafeName(name);

        var target = analysis.Loader switch
        {
            ModLoaderKind.BepInEx => $"BepInEx/plugins/{folder}/{fileName}",
            ModLoaderKind.MelonLoader when analysis.Component == ModComponentKind.Plugin => $"Plugins/{fileName}",
            ModLoaderKind.MelonLoader => $"Mods/{fileName}",
            _ => throw new InvalidDataException("Unsupported mod loader.")
        };

        var kind = PackageKindFor(analysis.Loader, analysis.Component, hasPackageLayout: false);
        var inspection = new PackageInspectionResult
        {
            IsValid = true,
            Name = name,
            Version = version,
            Author = author,
            Description = metadata?.Description ?? "",
            PackageKey = metadata?.PackageKey,
            Source = metadata?.Source ?? ModSourceType.LocalDll,
            Kind = kind,
            Loader = analysis.Loader,
            Component = analysis.Component,
            Dependencies = metadata?.Dependencies ?? Array.Empty<string>(),
            TargetFiles = new[] { target }
        };

        return new InstallPlan
        {
            Inspection = inspection,
            Files = new List<PlannedFile> { new(null, target, sourcePath) },
            Source = metadata?.Source ?? ModSourceType.LocalDll
        };
    }

    private static InstallPlan BuildZipPlan(string sourcePath, ModInstallMetadata? metadata)
    {
        try
        {
            using var archive = ZipFile.OpenRead(sourcePath);
            var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (files.Length == 0) return Invalid("The archive is empty.");

            var prefix = DetectSingleRootPrefix(files.Select(e => NormalizeArchivePath(e.FullName)));
            var manifestEntry = files.FirstOrDefault(e =>
                string.Equals(RemovePrefix(NormalizeArchivePath(e.FullName), prefix), "manifest.json", StringComparison.OrdinalIgnoreCase));

            var manifest = manifestEntry is null ? null : ReadManifest(manifestEntry);
            var name = metadata?.Name ?? manifest?.Name ?? Path.GetFileNameWithoutExtension(sourcePath);
            var version = metadata?.Version ?? manifest?.VersionNumber ?? "—";
            var description = metadata?.Description ?? manifest?.Description ?? "";
            var author = metadata?.Author ?? "Local";
            IReadOnlyList<string> dependencies = metadata?.Dependencies
                ?? (IReadOnlyList<string>?)manifest?.Dependencies
                ?? Array.Empty<string>();
            var packageKey = metadata?.PackageKey;
            if (packageKey is null && manifest is not null && TryInferThunderstoreIdentity(sourcePath, name, version, out var inferredKey, out var inferredAuthor))
            {
                packageKey = inferredKey;
                if (metadata?.Author is null) author = inferredAuthor;
            }

            var entries = files
                .Select(entry => new ArchiveFile(entry, RemovePrefix(NormalizeArchivePath(entry.FullName), prefix)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Relative) && !IsPackageDocument(item.Relative))
                .ToArray();

            var assemblyAnalyses = new List<ArchiveAssembly>();
            foreach (var item in entries.Where(item => item.Relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                var analysis = InspectArchiveAssembly(item.Entry);
                if (analysis.IsManaged)
                    assemblyAnalyses.Add(new ArchiveAssembly(item.Relative, analysis));
            }

            var assemblyLoaders = assemblyAnalyses
                .Where(item => item.Analysis.Loader != ModLoaderKind.Unknown)
                .Select(item => item.Analysis.Loader)
                .Distinct()
                .ToArray();
            if (assemblyLoaders.Length > 1)
                return Invalid("The archive contains both BepInEx and MelonLoader mod assemblies. Mixed-loader packages are not supported.");

            var hasBepPath = entries.Any(item => IsBepInExPackagePath(item.Relative));
            var hasMelonModsPath = entries.Any(item => NormalizeRelative(item.Relative).StartsWith("Mods/", StringComparison.OrdinalIgnoreCase));
            var hasBepDependency = dependencies.Any(IsBepInExDependency);
            var hasRootPluginsPath = entries.Any(item => NormalizeRelative(item.Relative).StartsWith("plugins/", StringComparison.OrdinalIgnoreCase));

            var loader = assemblyLoaders.FirstOrDefault();
            if (loader == ModLoaderKind.Unknown)
            {
                if (hasMelonModsPath)
                    loader = ModLoaderKind.MelonLoader;
                else if (hasBepPath || hasBepDependency || hasRootPluginsPath)
                    loader = ModLoaderKind.BepInEx;
            }

            if (loader == ModLoaderKind.MelonLoader && hasBepPath)
                return Invalid("The archive contains MelonLoader assemblies together with BepInEx install paths.");
            if (loader == ModLoaderKind.BepInEx && hasMelonModsPath)
                return Invalid("The archive contains BepInEx assemblies together with a MelonLoader Mods path.");

            var component = DetermineComponent(loader, entries, assemblyAnalyses);
            var folder = SafeName(name);
            var planned = new List<PlannedFile>();
            var warnings = new List<string>();
            var hasModSignal = loader != ModLoaderKind.Unknown;

            foreach (var item in entries)
            {
                if (IsRecognizedModPath(item.Relative)) hasModSignal = true;
                if (loader == ModLoaderKind.Unknown)
                    continue;

                var target = MapPackagePath(item.Relative, folder, loader, component);
                if (target is null)
                    continue;

                ValidateInstallTarget(target, loader);
                planned.Add(new PlannedFile(item.Entry.FullName, target, null));
            }

            var duplicate = planned
                .GroupBy(x => NormalizeRelative(x.TargetRelative), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                return Invalid($"The archive maps more than one file to {duplicate.Key}.");

            var kind = ClassifyPackage(loader, component, planned, dependencies, manifest is not null);
            if ((planned.Count == 0 || !hasModSignal) && kind != ModPackageKind.Modpack)
                return Invalid("No supported BepInEx or MelonLoader mod files were found in the archive.");

            if (manifest is null)
                warnings.Add("No Thunderstore manifest.json was found; metadata was inferred from the archive.");
            if (assemblyAnalyses.Any(item => item.Analysis.Loader == ModLoaderKind.Unknown && item.Analysis.IsManaged))
                warnings.Add("The archive contains managed DLLs that are not mod entry assemblies; they will be installed as package dependencies/assets.");

            var inspection = new PackageInspectionResult
            {
                IsValid = true,
                Name = name,
                Version = version,
                Author = author,
                Description = description,
                PackageKey = packageKey,
                Source = metadata?.Source ?? ModSourceType.LocalArchive,
                Kind = kind,
                Loader = loader,
                Component = component,
                Dependencies = dependencies.ToArray(),
                TargetFiles = planned.Select(x => x.TargetRelative).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Warnings = warnings
            };

            return new InstallPlan
            {
                Inspection = inspection,
                Files = planned,
                Source = metadata?.Source ?? ModSourceType.LocalArchive
            };
        }
        catch (InvalidDataException ex)
        {
            return Invalid(ex.Message);
        }
        catch (Exception ex)
        {
            return Invalid(ex.Message);
        }
    }

    private static ManifestData? ReadManifest(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            var manifest = new ManifestData
            {
                Name = GetString(root, "name"),
                VersionNumber = GetString(root, "version_number"),
                Description = GetString(root, "description")
            };

            if (root.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dependencies.EnumerateArray())
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) manifest.Dependencies.Add(value);
                }
            }

            return manifest;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private List<string> FindConflicts(string gameDirectory, IEnumerable<PlannedFile> files, ModInstallationRecord? existing)
    {
        var conflicts = new List<string>();
        foreach (var file in files)
        {
            var relative = NormalizeRelative(file.TargetRelative);
            var canonical = SafeCombine(gameDirectory, relative);
            if (IsConfigPath(relative)) continue;
            var actualExists = File.Exists(canonical) || File.Exists(canonical + ".disabled");
            if (!actualExists) continue;

            var owner = _registry.FindOwner(gameDirectory, relative);
            if (owner is null || existing is null || !owner.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase))
                conflicts.Add(relative);
        }
        return conflicts;
    }

    private static void StageFiles(string sourcePath, InstallPlan plan, string stageRoot)
    {
        if (Path.GetExtension(sourcePath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in plan.Files)
            {
                var target = SafeCombine(stageRoot, file.TargetRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(sourcePath, target, true);
            }
            return;
        }

        using var archive = ZipFile.OpenRead(sourcePath);
        foreach (var file in plan.Files)
        {
            if (file.ArchiveEntry is null) continue;
            var entry = archive.GetEntry(file.ArchiveEntry)
                        ?? archive.Entries.FirstOrDefault(e => e.FullName.Equals(file.ArchiveEntry, StringComparison.Ordinal));
            if (entry is null) throw new InvalidDataException($"Archive entry missing: {file.ArchiveEntry}");

            var target = SafeCombine(stageRoot, file.TargetRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = File.Create(target);
            input.CopyTo(output);
        }
    }

    private ModInstallationRecord CreateRecord(
        InstallPlan plan,
        string gameDirectory,
        string sourcePath,
        ModInstallationRecord? existing,
        IEnumerable<string> files,
        IEnumerable<string> userDataFiles)
    {
        return new ModInstallationRecord
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            GameDirectory = gameDirectory,
            Name = plan.Inspection.Name,
            Version = plan.Inspection.Version,
            Author = plan.Inspection.Author,
            Description = plan.Inspection.Description,
            PackageKey = plan.Inspection.PackageKey,
            Source = plan.Source,
            Kind = plan.Inspection.Kind,
            Loader = plan.Inspection.Loader,
            Component = plan.Inspection.Component,
            InstalledAt = DateTimeOffset.UtcNow,
            SourceFileName = Path.GetFileName(sourcePath),
            SourceHash = ComputeSha256(sourcePath),
            Dependencies = plan.Inspection.Dependencies.ToList(),
            Files = files.Select(NormalizeRelative).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            UserDataFiles = userDataFiles.Select(NormalizeRelative).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
        };
    }

    private ModInstallationRecord? FindExistingRecord(string gameDirectory, InstallPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.Inspection.PackageKey))
        {
            var byKey = _registry.FindByPackageKey(gameDirectory, plan.Inspection.PackageKey!);
            if (byKey is not null) return byKey;
        }

        return _registry.LoadAll().FirstOrDefault(x =>
            SamePath(x.GameDirectory, gameDirectory) &&
            x.Name.Equals(plan.Inspection.Name, StringComparison.OrdinalIgnoreCase) &&
            (x.Loader == ModLoaderKind.Unknown || x.Loader == plan.Inspection.Loader) &&
            x.Source != ModSourceType.External);
    }

    private void CacheSource(string sourcePath, ModInstallationRecord record, bool enabled)
    {
        if (!enabled) return;
        try
        {
            var directory = Path.Combine(_dataDirectory, "packages", record.Id);
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, Path.GetFileName(sourcePath));
            if (!SamePath(sourcePath, target)) File.Copy(sourcePath, target, true);
        }
        catch
        {
            // Cache failure must never make a successful game installation fail.
        }
    }

    private static bool IsRecordEnabled(ModInstallationRecord record, string gameDirectory)
    {
        var pluginDlls = record.Files.Where(IsLoadableDll).ToArray();
        if (pluginDlls.Length == 0) return true;
        return pluginDlls.Any(relative => File.Exists(SafeCombine(gameDirectory, relative)));
    }

    private static string? MapPackagePath(
        string relative,
        string packageFolder,
        ModLoaderKind loader,
        ModComponentKind component)
    {
        var normalized = NormalizeRelative(relative);

        if (loader == ModLoaderKind.BepInEx)
        {
            if (normalized.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
                return normalized;

            if (normalized.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase))
                return "BepInEx/" + normalized;

            return $"BepInEx/plugins/{packageFolder}/{normalized}";
        }

        if (loader == ModLoaderKind.MelonLoader)
        {
            if (normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase))
                return normalized;

            if (component == ModComponentKind.Unknown)
                throw new InvalidDataException("A mixed MelonLoader package must use explicit Mods/ and Plugins/ folders.");

            var root = component == ModComponentKind.Plugin ? "Plugins" : "Mods";
            return $"{root}/{packageFolder}/{normalized}";
        }

        return null;
    }

    private static void ValidateInstallTarget(string target, ModLoaderKind loader)
    {
        var normalized = NormalizeRelative(target);
        if (normalized.Contains("../", StringComparison.Ordinal) || normalized.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidDataException("The package contains an unsafe relative path.");

        var allowed = loader switch
        {
            ModLoaderKind.BepInEx => normalized.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ||
                                     normalized.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) ||
                                     normalized.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase),
            ModLoaderKind.MelonLoader => normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                                         normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (!allowed)
            throw new InvalidDataException($"The package tries to write to a protected or unsupported game path: {normalized}");

        if (normalized.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("MelonLoader/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("UserData/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Packages that replace the mod loader or loader user data are not supported.");
    }

    private static ModComponentKind DetermineComponent(
        ModLoaderKind loader,
        IReadOnlyList<ArchiveFile> entries,
        IReadOnlyList<ArchiveAssembly> assemblies)
    {
        if (loader == ModLoaderKind.MelonLoader)
        {
            var components = assemblies
                .Where(item => item.Analysis.Loader == ModLoaderKind.MelonLoader && item.Analysis.Component != ModComponentKind.Unknown)
                .Select(item => item.Analysis.Component)
                .Distinct()
                .ToArray();
            if (components.Length == 1) return components[0];
            if (components.Length > 1) return ModComponentKind.Unknown;

            var hasMods = entries.Any(item => NormalizeRelative(item.Relative).StartsWith("Mods/", StringComparison.OrdinalIgnoreCase));
            var hasPlugins = entries.Any(item => NormalizeRelative(item.Relative).StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase));
            if (hasMods && hasPlugins) return ModComponentKind.Unknown;
            if (hasPlugins) return ModComponentKind.Plugin;
            return ModComponentKind.Mod;
        }

        if (loader == ModLoaderKind.BepInEx)
        {
            if (entries.Any(item => NormalizeRelative(item.Relative).StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase) ||
                                    NormalizeRelative(item.Relative).StartsWith("patchers/", StringComparison.OrdinalIgnoreCase)))
                return ModComponentKind.Patcher;
            if (entries.Any(item => NormalizeRelative(item.Relative).Contains("/Maps/", StringComparison.OrdinalIgnoreCase) ||
                                    NormalizeRelative(item.Relative).StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)))
                return ModComponentKind.Content;
            return ModComponentKind.Plugin;
        }

        return ModComponentKind.Unknown;
    }

    private static ModPackageKind ClassifyPackage(
        ModLoaderKind loader,
        ModComponentKind component,
        IReadOnlyList<PlannedFile> files,
        IReadOnlyList<string> dependencies,
        bool hasManifest)
    {
        if (files.Count == 0 && dependencies.Count > 0 && hasManifest)
            return ModPackageKind.Modpack;

        if (loader == ModLoaderKind.MelonLoader)
            return PackageKindFor(loader, component, hasPackageLayout: true);

        if (files.Any(x => x.TargetRelative.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)))
            return ModPackageKind.ContentPack;
        if (files.Any(x => x.TargetRelative.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase)) ||
            files.Any(x => x.TargetRelative.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)))
            return ModPackageKind.BepInExPackage;
        return ModPackageKind.BepInExPlugin;
    }

    private static ModPackageKind PackageKindFor(ModLoaderKind loader, ModComponentKind component, bool hasPackageLayout)
    {
        if (loader == ModLoaderKind.BepInEx)
            return hasPackageLayout ? ModPackageKind.BepInExPackage : ModPackageKind.BepInExPlugin;

        if (loader == ModLoaderKind.MelonLoader)
        {
            if (component == ModComponentKind.Mod) return ModPackageKind.MelonMod;
            if (component == ModComponentKind.Plugin) return ModPackageKind.MelonPlugin;
            return ModPackageKind.MelonPackage;
        }

        return ModPackageKind.Unknown;
    }

    private static bool IsBepInExPackagePath(string relative)
    {
        var normalized = NormalizeRelative(relative);
        return normalized.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBepInExDependency(string dependency)
        => dependency.Contains("BepInEx", StringComparison.OrdinalIgnoreCase) ||
           dependency.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase);

    private static ManagedAssemblyInfo InspectArchiveAssembly(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var memory = new MemoryStream();
        input.CopyTo(memory);
        memory.Position = 0;
        return ManagedAssemblyInspector.Inspect(memory);
    }

    private static string DetectSingleRootPrefix(IEnumerable<string> paths)
    {
        var materialized = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (materialized.Length == 0) return "";
        if (materialized.Any(p => !p.Contains('/'))) return "";

        var roots = materialized
            .Select(p => p.Split('/', 2)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return roots.Length == 1 ? roots[0] + "/" : "";
    }

    private static string RemovePrefix(string path, string prefix)
        => !string.IsNullOrEmpty(prefix) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..]
            : path;

    private static bool TryInferThunderstoreIdentity(string sourcePath, string packageName, string version, out string packageKey, out string author)
    {
        packageKey = "";
        author = "Local";
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(version) || version == "—") return false;

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var versionSuffix = "-" + version;
        if (!stem.EndsWith(versionSuffix, StringComparison.OrdinalIgnoreCase)) return false;
        var withoutVersion = stem[..^versionSuffix.Length];
        var nameSuffix = "-" + packageName;
        if (!withoutVersion.EndsWith(nameSuffix, StringComparison.OrdinalIgnoreCase)) return false;

        var namespacePart = withoutVersion[..^nameSuffix.Length];
        if (string.IsNullOrWhiteSpace(namespacePart)) return false;
        packageKey = namespacePart + "-" + packageName;
        author = namespacePart;
        return true;
    }

    private static bool IsRecognizedModPath(string relative)
    {
        var normalized = NormalizeRelative(relative);
        return normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPackageDocument(string relative)
    {
        var file = relative.Replace('\\', '/');
        return file.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("README.txt", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("CHANGELOG.txt", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("LICENSE.md", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoadableDll(string relative)
    {
        var normalized = NormalizeRelative(relative);
        return (normalized.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase)) &&
               normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConfigPath(string relative)
        => NormalizeRelative(relative).StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);

    private static void BackupExisting(string path, string backupRoot, IDictionary<string, string> backups)
    {
        if (!File.Exists(path) || backups.ContainsKey(path)) return;
        var safeName = Convert.ToHexString(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(path))) + ".bak";
        var backup = Path.Combine(backupRoot, safeName);
        File.Copy(path, backup, true);
        backups[path] = backup;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = NormalizeRelative(relative).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A package path escaped the allowed directory.");
        return full;
    }

    private static string NormalizeArchivePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string NormalizeRelative(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "ImportedMod" : result;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private static void PruneEmptyDirectories(string gameDirectory, IEnumerable<string> files)
    {
        var directories = files
            .Select(f => Path.GetDirectoryName(SafeCombine(gameDirectory, f)))
            .Where(d => d is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(d => d!.Length)
            .ToArray();

        foreach (var directory in directories)
        {
            try
            {
                if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch { }
        }
    }

    private static PackageInspectionResult EnrichInspection(
        PackageInspectionResult source,
        ModSourceType installSource,
        string sourcePath,
        bool isUpgrade,
        string? existingVersion,
        IReadOnlyList<string> conflicts,
        bool missingLoader)
    {
        var warnings = source.Warnings.ToList();
        if (missingLoader)
            warnings.Add(source.Loader == ModLoaderKind.BepInEx ? "BepInEx is not installed or is not healthy." : "MelonLoader is not installed or is not healthy.");
        if (isUpgrade && !string.IsNullOrWhiteSpace(existingVersion))
            warnings.Add($"An existing managed installation ({existingVersion}) will be updated.");

        var risk = !source.IsValid || conflicts.Count > 0
            ? PackageRiskLevel.Blocked
            : warnings.Count > 0
                ? PackageRiskLevel.Warning
                : PackageRiskLevel.Safe;

        long packageSize = 0;
        try { if (File.Exists(sourcePath)) packageSize = new FileInfo(sourcePath).Length; } catch { }

        return new PackageInspectionResult
        {
            IsValid = source.IsValid,
            Name = source.Name,
            Version = source.Version,
            Author = source.Author,
            Description = source.Description,
            PackageKey = source.PackageKey,
            Source = installSource,
            Kind = source.Kind,
            Loader = source.Loader,
            Component = source.Component,
            Dependencies = source.Dependencies,
            TargetFiles = source.TargetFiles,
            Warnings = warnings,
            Conflicts = conflicts,
            Error = source.Error,
            RiskLevel = risk,
            MissingLoader = missingLoader,
            IsUpgrade = isUpgrade,
            ExistingVersion = existingVersion,
            PackageSize = packageSize,
            TargetSummary = GetTargetSummary(source.TargetFiles)
        };
    }

    private static string GetTargetSummary(IReadOnlyList<string> targetFiles)
    {
        if (targetFiles.Count == 0) return "—";
        var normalized = targetFiles.Select(NormalizeRelative).ToArray();
        if (normalized.All(x => x.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase))) return "BepInEx/plugins";
        if (normalized.All(x => x.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase))) return "BepInEx/patchers";
        if (normalized.All(x => x.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))) return "Mods";
        if (normalized.All(x => x.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase))) return "Plugins";
        return normalized[0].Split('/')[0];
    }

    private static InstallPlan Invalid(string error)
        => new()
        {
            Inspection = new PackageInspectionResult
            {
                IsValid = false,
                Error = error
            }
        };

    private sealed class InstallPlan
    {
        public PackageInspectionResult Inspection { get; init; } = new();
        public List<PlannedFile> Files { get; init; } = new();
        public ModSourceType Source { get; init; } = ModSourceType.LocalArchive;
    }

    private sealed record PlannedFile(string? ArchiveEntry, string TargetRelative, string? DirectSource);
    private sealed record ArchiveFile(ZipArchiveEntry Entry, string Relative);
    private sealed record ArchiveAssembly(string Relative, ManagedAssemblyInfo Analysis);

    private sealed class ManifestData
    {
        public string? Name { get; set; }
        public string? VersionNumber { get; set; }
        public string? Description { get; set; }
        public List<string> Dependencies { get; } = new();
    }
}
