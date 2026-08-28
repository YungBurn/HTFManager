using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Profiles;

public sealed class ProfileService(ISettingsStore settingsStore) : IProfileService
{
    private readonly string _directory = Path.Combine(settingsStore.DataDirectory, "profiles");
    private readonly string _snapshotRoot = Path.Combine(settingsStore.DataDirectory, "profile-snapshots");
    private readonly string _recoveryRoot = Path.Combine(settingsStore.DataDirectory, "profile-recovery");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions PortableJsonOptions = CreatePortableJsonOptions();
    private const string PortableFormat = "HTFManager.Profile";
    private const int PortableSchemaVersion = 1;
    private const string ExportedWithVersion = "0.3.5.1";
    private const long MaxPortableArchiveBytes = 32L * 1024L * 1024L;
    private const long MaxPortableConfigBytes = 4L * 1024L * 1024L;

    public IReadOnlyList<ModProfile> LoadProfiles()
    {
        Directory.CreateDirectory(_directory);
        var profiles = new List<ModProfile>();

        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<ModProfile>(File.ReadAllText(file), JsonOptions);
                if (profile is not null)
                {
                    profile.ModStates = new Dictionary<string, bool>(
                        profile.ModStates ?? new Dictionary<string, bool>(),
                        StringComparer.OrdinalIgnoreCase);
                    profile.ConfigurationSnapshots ??= new List<ProfileConfigurationSnapshot>();
                    profile.UnresolvedMods ??= new List<ProfileModRequirement>();
                    profiles.Add(profile);
                }
            }
            catch { }
        }

        return profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public ModProfile Capture(string name, IReadOnlyList<InstalledMod> mods)
    {
        return new ModProfile
        {
            Name = name,
            ModStates = mods.ToDictionary(m => m.Id, m => m.Enabled, StringComparer.OrdinalIgnoreCase)
        };
    }

    public void Save(ModProfile profile)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ProfilePath(profile.Name), JsonSerializer.Serialize(profile, JsonOptions));
    }

    public void Delete(ModProfile profile)
    {
        var path = ProfilePath(profile.Name);
        if (File.Exists(path))
            File.Delete(path);

        TryDeleteDirectory(ProfileSnapshotDirectory(profile.Name));
        TryDeleteDirectory(Path.Combine(_recoveryRoot, SafeName(profile.Name)));
    }

    public ProfilePackageInspection InspectPortablePackage(string packagePath, IReadOnlyList<InstalledMod> installedMods)
    {
        if (!File.Exists(packagePath))
            return ProfilePackageInspection.Invalid("Profile package does not exist.");

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var validation = ValidatePortableArchive(archive, out var manifest);
            if (validation is not null || manifest is null)
                return ProfilePackageInspection.Invalid(validation ?? "Profile manifest is invalid.");

            var previews = manifest.Mods
                .Select(requirement =>
                {
                    var match = MatchRequirement(requirement, installedMods);
                    return new ProfilePackageModPreview
                    {
                        Requirement = CloneRequirement(requirement),
                        Matched = match is not null,
                        MatchedInstalledModId = match?.Id,
                        MatchedInstalledModName = match?.Name,
                        MatchedInstalledVersion = match?.Version,
                        VersionMatches = match is null || VersionsCompatible(requirement.Version, match.Version)
                    };
                })
                .ToArray();

            return new ProfilePackageInspection
            {
                IsValid = true,
                ProfileName = manifest.ProfileName,
                ImportName = MakeUniqueProfileName(manifest.ProfileName),
                SchemaVersion = manifest.SchemaVersion,
                ExportedWithVersion = manifest.ExportedWithVersion,
                ExportedUtc = manifest.ExportedUtc,
                Mods = previews,
                ConfigurationCount = manifest.Configurations.Count,
                ConfigurationBytes = manifest.Configurations.Sum(item => item.Size)
            };
        }
        catch (Exception ex)
        {
            return ProfilePackageInspection.Invalid(ex.Message);
        }
    }

    public ProfileOperationResult ExportPortablePackage(
        ModProfile profile,
        IReadOnlyList<InstalledMod> installedMods,
        string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return ProfileOperationResult.Fail("Export destination is not available.");

        var finalPath = destinationPath.EndsWith(".htfprofile", StringComparison.OrdinalIgnoreCase)
            ? destinationPath
            : destinationPath + ".htfprofile";
        var tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            var installedById = installedMods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
            var requirements = new List<ProfileModRequirement>();
            var portableByLocalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in profile.ModStates.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (installedById.TryGetValue(pair.Key, out var mod))
                {
                    var requirement = RequirementFromInstalled(mod, pair.Value);
                    requirements.Add(requirement);
                    portableByLocalId[pair.Key] = requirement.PortableId;
                }
                else
                {
                    var requirement = new ProfileModRequirement
                    {
                        PortableId = "legacy-" + HashText(pair.Key)[..24].ToLowerInvariant(),
                        Name = pair.Key,
                        Enabled = pair.Value
                    };
                    requirements.Add(requirement);
                    portableByLocalId[pair.Key] = requirement.PortableId;
                }
            }

            foreach (var unresolved in profile.UnresolvedMods)
            {
                if (requirements.Any(item => item.PortableId.Equals(unresolved.PortableId, StringComparison.OrdinalIgnoreCase)))
                    continue;
                requirements.Add(CloneRequirement(unresolved));
            }

            var manifest = new PortableProfileManifest
            {
                Format = PortableFormat,
                SchemaVersion = PortableSchemaVersion,
                ExportedWithVersion = ExportedWithVersion,
                ExportedUtc = DateTimeOffset.UtcNow,
                ProfileName = profile.Name,
                Mods = requirements
            };

            var snapshotDirectory = ProfileSnapshotDirectory(profile.Name);
            foreach (var snapshot in profile.ConfigurationSnapshots)
            {
                var source = Path.Combine(snapshotDirectory, snapshot.SnapshotFileName);
                if (!File.Exists(source))
                    return ProfileOperationResult.Fail($"Snapshot file is missing: {snapshot.DisplayName}");
                if (!HashFile(source).Equals(snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
                    return ProfileOperationResult.Fail($"Snapshot integrity check failed: {snapshot.DisplayName}");
                if (!IsAllowedPortableConfigPath(snapshot.RelativePath))
                    return ProfileOperationResult.Fail($"Configuration path cannot be shared: {snapshot.RelativePath}");

                var portableId = snapshot.AssociatedPortableModId;
                if (string.IsNullOrWhiteSpace(portableId) && !string.IsNullOrWhiteSpace(snapshot.AssociatedModId))
                    portableByLocalId.TryGetValue(snapshot.AssociatedModId!, out portableId);

                var extension = Path.GetExtension(snapshot.SnapshotFileName);
                if (string.IsNullOrWhiteSpace(extension)) extension = ".cfg";
                var archiveEntry = "configs/" + HashText(snapshot.RelativePath)[..32].ToLowerInvariant() + extension;
                var info = new FileInfo(source);
                if (info.Length > MaxPortableConfigBytes)
                    return ProfileOperationResult.Fail($"Configuration snapshot is too large: {snapshot.DisplayName}");

                if (string.IsNullOrWhiteSpace(portableId))
                    return ProfileOperationResult.Fail($"Configuration snapshot is not linked to a profile mod: {snapshot.DisplayName}");

                manifest.Configurations.Add(new PortableProfileConfiguration
                {
                    DisplayName = snapshot.DisplayName,
                    RelativePath = NormalizeRelative(snapshot.RelativePath),
                    ArchiveEntry = archiveEntry,
                    Sha256 = snapshot.Sha256,
                    Size = info.Length,
                    Loader = snapshot.Loader,
                    AssociatedPortableModId = portableId,
                    CapturedUtc = snapshot.CapturedUtc
                });
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(finalPath));
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var stream = manifestEntry.Open())
                    JsonSerializer.Serialize(stream, manifest, PortableJsonOptions);

                foreach (var config in manifest.Configurations)
                {
                    var snapshot = profile.ConfigurationSnapshots.First(item =>
                        NormalizeRelative(item.RelativePath).Equals(config.RelativePath, StringComparison.OrdinalIgnoreCase));
                    var source = Path.Combine(snapshotDirectory, snapshot.SnapshotFileName);
                    var entry = archive.CreateEntry(config.ArchiveEntry, CompressionLevel.Optimal);
                    using var input = File.OpenRead(source);
                    using var output = entry.Open();
                    input.CopyTo(output);
                }
            }

            File.Move(tempPath, finalPath, true);
            return ProfileOperationResult.Ok("Profile exported.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return ProfileOperationResult.Fail(ex.Message);
        }
    }

    public ProfileOperationResult ImportPortablePackage(
        string packagePath,
        IReadOnlyList<InstalledMod> installedMods,
        string? importName = null)
    {
        var inspection = InspectPortablePackage(packagePath, installedMods);
        if (!inspection.IsValid)
            return ProfileOperationResult.Fail(inspection.Error ?? "Profile package is invalid.");

        var name = string.IsNullOrWhiteSpace(importName) ? inspection.ImportName : importName.Trim();
        if (LoadProfiles().Any(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return ProfileOperationResult.Fail("A profile with this name already exists.");
        var finalSnapshotDirectory = ProfileSnapshotDirectory(name);
        var tempSnapshotDirectory = finalSnapshotDirectory + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var validation = ValidatePortableArchive(archive, out var manifest);
            if (validation is not null || manifest is null)
                return ProfileOperationResult.Fail(validation ?? "Profile package is invalid.");

            var profile = new ModProfile { Name = name };
            var matchedByPortableId = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
            var resolvedPortableIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var requirement in manifest.Mods)
            {
                var match = MatchRequirement(requirement, installedMods);
                if (match is not null)
                {
                    profile.ModStates[match.Id] = requirement.Enabled;
                    matchedByPortableId[requirement.PortableId] = match;
                    resolvedPortableIds[requirement.PortableId] = RequirementFromInstalled(match, requirement.Enabled).PortableId;
                }
                else
                {
                    profile.UnresolvedMods.Add(CloneRequirement(requirement));
                }
            }

            if (manifest.Configurations.Count > 0)
            {
                Directory.CreateDirectory(tempSnapshotDirectory);
                foreach (var config in manifest.Configurations)
                {
                    if (!IsAllowedPortableConfigPath(config.RelativePath))
                        throw new InvalidDataException($"Unsafe shared configuration path: {config.RelativePath}");

                    var entry = archive.GetEntry(config.ArchiveEntry)
                        ?? throw new InvalidDataException($"Shared configuration is missing: {config.DisplayName}");
                    var extension = Path.GetExtension(config.RelativePath);
                    if (string.IsNullOrWhiteSpace(extension)) extension = ".cfg";
                    var snapshotFileName = HashText(config.RelativePath) + extension;
                    var snapshotPath = Path.Combine(tempSnapshotDirectory, snapshotFileName);
                    using (var input = entry.Open())
                    using (var output = File.Create(snapshotPath))
                        input.CopyTo(output);

                    var actualHash = HashFile(snapshotPath);
                    if (!actualHash.Equals(config.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Shared configuration integrity check failed: {config.DisplayName}");

                    string? associatedModId = null;
                    string? associatedPortableModId = config.AssociatedPortableModId;
                    if (!string.IsNullOrWhiteSpace(config.AssociatedPortableModId))
                    {
                        associatedModId = matchedByPortableId.TryGetValue(config.AssociatedPortableModId!, out var matched)
                            ? matched.Id
                            : "unresolved:" + config.AssociatedPortableModId;
                        if (resolvedPortableIds.TryGetValue(config.AssociatedPortableModId!, out var localPortableId))
                            associatedPortableModId = localPortableId;
                    }

                    profile.ConfigurationSnapshots.Add(new ProfileConfigurationSnapshot
                    {
                        Id = "imported:" + HashText(config.RelativePath)[..24].ToLowerInvariant(),
                        DisplayName = config.DisplayName,
                        RelativePath = NormalizeRelative(config.RelativePath),
                        SnapshotFileName = snapshotFileName,
                        Sha256 = actualHash,
                        Loader = config.Loader,
                        AssociatedModId = associatedModId,
                        AssociatedPortableModId = associatedPortableModId,
                        CapturedUtc = config.CapturedUtc
                    });
                }

                profile.ConfigurationSnapshotCapturedUtc = manifest.Configurations
                    .Select(item => (DateTime?)item.CapturedUtc)
                    .OrderByDescending(item => item)
                    .FirstOrDefault();

                Directory.CreateDirectory(Path.GetDirectoryName(finalSnapshotDirectory)!);
                if (Directory.Exists(finalSnapshotDirectory))
                    throw new IOException("Target profile snapshot directory already exists.");
                Directory.Move(tempSnapshotDirectory, finalSnapshotDirectory);
            }

            Save(profile);
            return ProfileOperationResult.Ok("Profile imported.", profile.ConfigurationSnapshots.Count);
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(tempSnapshotDirectory);
            TryDeleteDirectory(finalSnapshotDirectory);
            try
            {
                var profilePath = ProfilePath(name);
                if (File.Exists(profilePath)) File.Delete(profilePath);
            }
            catch { }
            return ProfileOperationResult.Fail(ex.Message);
        }
    }

    public ProfileOperationResult ResolveMissingMods(ModProfile profile, IReadOnlyList<InstalledMod> installedMods)
    {
        var resolved = 0;
        foreach (var requirement in profile.UnresolvedMods.ToArray())
        {
            var match = MatchRequirement(requirement, installedMods);
            if (match is null) continue;

            profile.ModStates[match.Id] = requirement.Enabled;
            var localPortableId = RequirementFromInstalled(match, requirement.Enabled).PortableId;
            foreach (var snapshot in profile.ConfigurationSnapshots.Where(item =>
                         !string.IsNullOrWhiteSpace(item.AssociatedPortableModId) &&
                         item.AssociatedPortableModId!.Equals(requirement.PortableId, StringComparison.OrdinalIgnoreCase)))
            {
                snapshot.AssociatedModId = match.Id;
                snapshot.AssociatedPortableModId = localPortableId;
            }

            profile.UnresolvedMods.Remove(requirement);
            resolved++;
        }

        Save(profile);
        return ProfileOperationResult.Ok($"Resolved {resolved} missing mod(s).");
    }

    public void RemoveMissingMod(ModProfile profile, string portableId)
    {
        if (string.IsNullOrWhiteSpace(portableId)) return;
        var removed = profile.UnresolvedMods.RemoveAll(item =>
            item.PortableId.Equals(portableId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;

        var snapshotDirectory = ProfileSnapshotDirectory(profile.Name);
        var snapshots = profile.ConfigurationSnapshots.Where(item =>
            !string.IsNullOrWhiteSpace(item.AssociatedPortableModId) &&
            item.AssociatedPortableModId!.Equals(portableId, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var snapshot in snapshots)
        {
            profile.ConfigurationSnapshots.Remove(snapshot);
            try
            {
                var path = Path.Combine(snapshotDirectory, snapshot.SnapshotFileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
        Save(profile);
    }

    public ProfileOperationResult CaptureConfigurationSnapshots(
        ModProfile profile,
        IReadOnlyList<ModConfigurationDocument> configurations,
        string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return ProfileOperationResult.Fail("Game directory is not available.");

        var includedModIds = profile.ModStates.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eligible = configurations
            .Where(document => !document.IsLoaderConfiguration)
            .Where(document => !string.IsNullOrWhiteSpace(document.AssociatedModId))
            .Where(document => includedModIds.Contains(document.AssociatedModId!))
            .Where(document => File.Exists(document.FilePath))
            .OrderBy(document => document.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (eligible.Length == 0)
        {
            ClearConfigurationSnapshots(profile);
            return ProfileOperationResult.Ok("No mod configuration files were available to capture.");
        }

        var finalDirectory = ProfileSnapshotDirectory(profile.Name);
        var tempDirectory = finalDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
        var capturedUtc = DateTime.UtcNow;
        var snapshots = new List<ProfileConfigurationSnapshot>();

        try
        {
            Directory.CreateDirectory(tempDirectory);
            foreach (var document in eligible)
            {
                if (!TryGetSafeRelativePath(gameDirectory, document.FilePath, out var relativePath))
                    continue;

                var extension = Path.GetExtension(document.FilePath);
                if (string.IsNullOrWhiteSpace(extension)) extension = ".cfg";
                var snapshotFileName = HashText(relativePath) + extension;
                var snapshotPath = Path.Combine(tempDirectory, snapshotFileName);
                File.Copy(document.FilePath, snapshotPath, true);

                snapshots.Add(new ProfileConfigurationSnapshot
                {
                    Id = document.Id,
                    DisplayName = document.DisplayName,
                    RelativePath = NormalizeRelative(relativePath),
                    SnapshotFileName = snapshotFileName,
                    Sha256 = HashFile(snapshotPath),
                    Loader = document.Loader,
                    AssociatedModId = document.AssociatedModId,
                    CapturedUtc = capturedUtc
                });
            }

            if (snapshots.Count == 0)
            {
                TryDeleteDirectory(tempDirectory);
                ClearConfigurationSnapshots(profile);
                return ProfileOperationResult.Ok("No safe mod configuration files were available to capture.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
            var previousDirectory = finalDirectory + ".previous-" + Guid.NewGuid().ToString("N");
            var movedPrevious = false;
            try
            {
                if (Directory.Exists(finalDirectory))
                {
                    Directory.Move(finalDirectory, previousDirectory);
                    movedPrevious = true;
                }
                Directory.Move(tempDirectory, finalDirectory);
                if (movedPrevious) TryDeleteDirectory(previousDirectory);
            }
            catch
            {
                TryDeleteDirectory(finalDirectory);
                if (movedPrevious && Directory.Exists(previousDirectory))
                    Directory.Move(previousDirectory, finalDirectory);
                throw;
            }

            profile.ConfigurationSnapshots = snapshots;
            profile.ConfigurationSnapshotCapturedUtc = capturedUtc;
            Save(profile);
            return ProfileOperationResult.Ok("Configuration snapshot captured.", snapshots.Count);
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(tempDirectory);
            return ProfileOperationResult.Fail(ex.Message);
        }
    }

    public ProfileOperationResult ClearConfigurationSnapshots(ModProfile profile)
    {
        TryDeleteDirectory(ProfileSnapshotDirectory(profile.Name));
        profile.ConfigurationSnapshots.Clear();
        profile.ConfigurationSnapshotCapturedUtc = null;
        Save(profile);
        return ProfileOperationResult.Ok("Configuration snapshot cleared.");
    }

    public ProfileOperationResult Apply(
        ModProfile profile,
        IReadOnlyList<InstalledMod> mods,
        IModService modService,
        string? gameDirectory)
    {
        if (profile.UnresolvedMods.Count > 0)
            return ProfileOperationResult.Fail("Profile contains unresolved shared mods.");

        var originalStates = mods.ToDictionary(mod => mod.Id, mod => mod.Enabled, StringComparer.OrdinalIgnoreCase);
        var changedMods = new List<InstalledMod>();
        var configActions = new List<ConfigApplyAction>();
        string? recoveryDirectory = null;

        try
        {
            if (profile.ConfigurationSnapshots.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
                    return ProfileOperationResult.Fail("Game directory is not available.");

                var installedIds = mods.Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var snapshotDirectory = ProfileSnapshotDirectory(profile.Name);
                foreach (var snapshot in profile.ConfigurationSnapshots)
                {
                    if (!string.IsNullOrWhiteSpace(snapshot.AssociatedModId) &&
                        (!profile.ModStates.ContainsKey(snapshot.AssociatedModId!) ||
                         !installedIds.Contains(snapshot.AssociatedModId!)))
                        continue;

                    var source = Path.Combine(snapshotDirectory, snapshot.SnapshotFileName);
                    if (!File.Exists(source))
                        return ProfileOperationResult.Fail($"Snapshot file is missing: {snapshot.DisplayName}");
                    if (!HashFile(source).Equals(snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
                        return ProfileOperationResult.Fail($"Snapshot integrity check failed: {snapshot.DisplayName}");
                    if (!TryResolveSafeTarget(gameDirectory!, snapshot.RelativePath, out var target))
                        return ProfileOperationResult.Fail($"Unsafe configuration path: {snapshot.RelativePath}");

                    configActions.Add(new ConfigApplyAction(snapshot, source, target));
                }
            }

            if (configActions.Count > 0)
            {
                recoveryDirectory = CreateRecoveryDirectory(profile.Name);
                foreach (var action in configActions)
                {
                    action.ExistedBefore = File.Exists(action.TargetPath);
                    if (!action.ExistedBefore) continue;
                    action.RecoveryPath = Path.Combine(recoveryDirectory, "files", action.Snapshot.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(action.RecoveryPath)!);
                    File.Copy(action.TargetPath, action.RecoveryPath, true);
                }

                var manifest = configActions.Select(action => new RecoveryItem
                {
                    RelativePath = action.Snapshot.RelativePath,
                    ExistedBefore = action.ExistedBefore
                }).ToArray();
                File.WriteAllText(
                    Path.Combine(recoveryDirectory, "recovery.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions));
            }

            // A profile is an isolated set. Mods not present in the profile are disabled,
            // but never uninstalled or removed from disk.
            foreach (var mod in mods)
            {
                var targetEnabled = profile.ModStates.TryGetValue(mod.Id, out var enabled) && enabled;
                if (mod.Enabled == targetEnabled) continue;
                if (!modService.SetEnabled(mod, targetEnabled))
                    throw new IOException($"Could not change mod state: {mod.Name}");
                changedMods.Add(mod);
            }

            foreach (var action in configActions)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(action.TargetPath)!);
                File.Copy(action.SourcePath, action.TargetPath, true);
            }

            PruneRecoveries(profile.Name, 10);
            return ProfileOperationResult.Ok("Profile applied.", configActions.Count, recoveryDirectory);
        }
        catch (Exception ex)
        {
            foreach (var action in configActions.AsEnumerable().Reverse())
            {
                try
                {
                    if (action.ExistedBefore && !string.IsNullOrWhiteSpace(action.RecoveryPath) && File.Exists(action.RecoveryPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(action.TargetPath)!);
                        File.Copy(action.RecoveryPath, action.TargetPath, true);
                    }
                    else if (!action.ExistedBefore && File.Exists(action.TargetPath))
                    {
                        File.Delete(action.TargetPath);
                    }
                }
                catch { }
            }

            foreach (var mod in changedMods.AsEnumerable().Reverse())
            {
                try
                {
                    if (originalStates.TryGetValue(mod.Id, out var enabled))
                        RollbackModState(mod, enabled, modService);
                }
                catch { }
            }

            return ProfileOperationResult.Fail(ex.Message, recoveryDirectory);
        }
    }

    private string ProfilePath(string profileName)
        => Path.Combine(_directory, SafeName(profileName) + ".json");

    private string ProfileSnapshotDirectory(string profileName)
        => Path.Combine(_snapshotRoot, SafeName(profileName));

    private string CreateRecoveryDirectory(string profileName)
    {
        var directory = Path.Combine(
            _recoveryRoot,
            SafeName(profileName),
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void PruneRecoveries(string profileName, int keep)
    {
        var directory = Path.Combine(_recoveryRoot, SafeName(profileName));
        if (!Directory.Exists(directory)) return;
        try
        {
            foreach (var old in Directory.EnumerateDirectories(directory)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                         .Skip(Math.Max(1, keep)))
                TryDeleteDirectory(old);
        }
        catch { }
    }


    private static void RollbackModState(InstalledMod mod, bool originalEnabled, IModService modService)
    {
        if (mod.IsManaged)
        {
            modService.SetEnabled(mod, originalEnabled);
            return;
        }

        var originalPath = mod.FilePath;
        if (originalEnabled)
        {
            var canonical = originalPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? originalPath[..^".disabled".Length]
                : originalPath;
            var disabled = canonical + ".disabled";
            if (!File.Exists(canonical) && File.Exists(disabled))
                File.Move(disabled, canonical);
        }
        else
        {
            var disabled = originalPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? originalPath
                : originalPath + ".disabled";
            var canonical = disabled[..^".disabled".Length];
            if (!File.Exists(disabled) && File.Exists(canonical))
                File.Move(canonical, disabled);
        }
    }

    private static bool TryGetSafeRelativePath(string root, string path, out string relative)
    {
        relative = "";
        try
        {
            var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
            var pathFull = Path.GetFullPath(path);
            if (!pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;
            relative = Path.GetRelativePath(rootFull, pathFull);
            return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }
        catch { return false; }
    }

    private static bool TryResolveSafeTarget(string root, string relativePath, out string target)
    {
        target = "";
        try
        {
            if (Path.IsPathRooted(relativePath)) return false;
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
            var targetFull = Path.GetFullPath(Path.Combine(rootFull, normalized));
            if (!targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;
            target = targetFull;
            return true;
        }
        catch { return false; }
    }

    private static string SafeName(string profileName)
    {
        var safeName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safeName) ? "Profile" : safeName;
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');
    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(value)));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { }
    }


    private string MakeUniqueProfileName(string requested)
    {
        var baseName = string.IsNullOrWhiteSpace(requested) ? "Imported Profile" : requested.Trim();
        var existing = LoadProfiles().Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName)) return baseName;

        var candidate = baseName + " (Imported)";
        if (!existing.Contains(candidate)) return candidate;
        for (var index = 2; index < 1000; index++)
        {
            candidate = $"{baseName} (Imported {index})";
            if (!existing.Contains(candidate)) return candidate;
        }
        return baseName + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    }

    private static ProfileModRequirement RequirementFromInstalled(InstalledMod mod, bool enabled)
    {
        var identity = !string.IsNullOrWhiteSpace(mod.PackageKey)
            ? "package|" + mod.PackageKey
            : $"local|{mod.Loader}|{mod.Component}|{mod.Name}|{Path.GetFileName(mod.FilePath)}";
        return new ProfileModRequirement
        {
            PortableId = "mod-" + HashText(identity)[..24].ToLowerInvariant(),
            Name = mod.Name,
            Version = mod.Version,
            Author = mod.Author,
            PackageKey = mod.PackageKey,
            FileName = Path.GetFileName(mod.FilePath).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase),
            Source = mod.Source,
            Loader = mod.Loader,
            Component = mod.Component,
            Enabled = enabled
        };
    }

    private static ProfileModRequirement CloneRequirement(ProfileModRequirement source)
        => new()
        {
            PortableId = source.PortableId,
            Name = source.Name,
            Version = source.Version,
            Author = source.Author,
            PackageKey = source.PackageKey,
            FileName = source.FileName,
            Source = source.Source,
            Loader = source.Loader,
            Component = source.Component,
            Enabled = source.Enabled
        };

    private static bool VersionsCompatible(string required, string installed)
    {
        if (string.IsNullOrWhiteSpace(required) || required == "—") return true;
        if (string.IsNullOrWhiteSpace(installed) || installed == "—") return false;
        return required.Equals(installed, StringComparison.OrdinalIgnoreCase);
    }

    private static InstalledMod? MatchRequirement(ProfileModRequirement requirement, IReadOnlyList<InstalledMod> installedMods)
    {
        if (!string.IsNullOrWhiteSpace(requirement.PackageKey))
        {
            var byPackage = installedMods.FirstOrDefault(mod =>
                !string.IsNullOrWhiteSpace(mod.PackageKey) &&
                mod.PackageKey!.Equals(requirement.PackageKey, StringComparison.OrdinalIgnoreCase));
            if (byPackage is not null) return byPackage;
        }

        var candidates = installedMods
            .Where(mod => requirement.Loader == ModLoaderKind.Unknown || mod.Loader == requirement.Loader)
            .Where(mod => requirement.Component == ModComponentKind.Unknown || mod.Component == requirement.Component)
            .Where(mod => mod.Name.Equals(requirement.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 1) return candidates[0];
        if (candidates.Length > 1)
        {
            var sameVersion = candidates.FirstOrDefault(mod =>
                !string.IsNullOrWhiteSpace(requirement.Version) &&
                mod.Version.Equals(requirement.Version, StringComparison.OrdinalIgnoreCase));
            if (sameVersion is not null) return sameVersion;
        }

        if (!string.IsNullOrWhiteSpace(requirement.FileName))
        {
            var byFile = installedMods.FirstOrDefault(mod =>
                (requirement.Loader == ModLoaderKind.Unknown || mod.Loader == requirement.Loader) &&
                Path.GetFileName(mod.FilePath).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase)
                    .Equals(requirement.FileName, StringComparison.OrdinalIgnoreCase));
            if (byFile is not null) return byFile;
        }

        return null;
    }

    private static string? ValidatePortableArchive(ZipArchive archive, out PortableProfileManifest? manifest)
    {
        manifest = null;
        if (archive.Entries.Count > 512) return "Profile package contains too many files.";
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeRelative(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            if (Path.IsPathRooted(entry.FullName) || normalized.StartsWith("../", StringComparison.Ordinal) ||
                normalized.Contains("/../", StringComparison.Ordinal) || normalized.Contains(':'))
                return "Profile package contains an unsafe path.";
            total += Math.Max(0, entry.Length);
            if (total > MaxPortableArchiveBytes) return "Profile package is too large.";
        }

        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null || manifestEntry.Length <= 0 || manifestEntry.Length > 2 * 1024 * 1024)
            return "Profile manifest is missing or invalid.";

        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<PortableProfileManifest>(stream, PortableJsonOptions);
        if (manifest is null) return "Profile manifest could not be read.";
        if (!manifest.Format.Equals(PortableFormat, StringComparison.Ordinal)) return "Unsupported profile package format.";
        if (manifest.SchemaVersion != PortableSchemaVersion) return "Unsupported profile package schema version.";
        if (string.IsNullOrWhiteSpace(manifest.ProfileName)) return "Profile name is missing.";
        if (manifest.Mods.Count > 512 || manifest.Configurations.Count > 256) return "Profile package contains too many entries.";
        if (manifest.Mods.Any(item => string.IsNullOrWhiteSpace(item.PortableId) || string.IsNullOrWhiteSpace(item.Name)))
            return "Profile package contains invalid mod metadata.";
        if (manifest.Mods.GroupBy(item => item.PortableId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return "Profile package contains duplicate mod identifiers.";

        var portableIds = manifest.Mods.Select(item => item.PortableId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var config in manifest.Configurations)
        {
            if (string.IsNullOrWhiteSpace(config.AssociatedPortableModId))
                return "Profile package contains an unassociated configuration snapshot.";
            if (!portableIds.Contains(config.AssociatedPortableModId!))
                return "Profile package contains a configuration linked to an unknown mod.";
            if (!IsAllowedPortableConfigPath(config.RelativePath))
                return $"Profile package contains an unsupported configuration path: {config.RelativePath}";
            var entryName = NormalizeRelative(config.ArchiveEntry);
            if (!entryName.StartsWith("configs/", StringComparison.OrdinalIgnoreCase))
                return "Profile package contains an invalid configuration entry.";
            var entry = archive.GetEntry(config.ArchiveEntry);
            if (entry is null) return $"Profile configuration is missing: {config.DisplayName}";
            if (entry.Length < 0 || entry.Length > MaxPortableConfigBytes || config.Size != entry.Length)
                return $"Profile configuration has an invalid size: {config.DisplayName}";
            using var stream = entry.Open();
            var hash = HashStream(stream);
            if (!hash.Equals(config.Sha256, StringComparison.OrdinalIgnoreCase))
                return $"Profile configuration integrity check failed: {config.DisplayName}";
        }

        return null;
    }

    private static bool IsAllowedPortableConfigPath(string relativePath)
    {
        var normalized = NormalizeRelative(relativePath).TrimStart('/');
        if (!normalized.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.Equals("BepInEx/config/BepInEx.cfg", StringComparison.OrdinalIgnoreCase)) return false;
        return !normalized.Contains("../", StringComparison.Ordinal) && !normalized.Contains(':');
    }

    private static string HashStream(Stream stream)
        => Convert.ToHexString(SHA256.HashData(stream));

    private static JsonSerializerOptions CreatePortableJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class PortableProfileManifest
    {
        public string Format { get; set; } = PortableFormat;
        public int SchemaVersion { get; set; } = PortableSchemaVersion;
        public string ExportedWithVersion { get; set; } = ProfileService.ExportedWithVersion;
        public DateTimeOffset ExportedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string ProfileName { get; set; } = "Imported Profile";
        public List<ProfileModRequirement> Mods { get; set; } = new();
        public List<PortableProfileConfiguration> Configurations { get; set; } = new();
    }

    private sealed class PortableProfileConfiguration
    {
        public string DisplayName { get; set; } = "Configuration";
        public string RelativePath { get; set; } = "";
        public string ArchiveEntry { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
        public ModLoaderKind Loader { get; set; } = ModLoaderKind.Unknown;
        public string? AssociatedPortableModId { get; set; }
        public DateTime CapturedUtc { get; set; }
    }

    private sealed class ConfigApplyAction(ProfileConfigurationSnapshot snapshot, string sourcePath, string targetPath)
    {
        public ProfileConfigurationSnapshot Snapshot { get; } = snapshot;
        public string SourcePath { get; } = sourcePath;
        public string TargetPath { get; } = targetPath;
        public bool ExistedBefore { get; set; }
        public string? RecoveryPath { get; set; }
    }

    private sealed class RecoveryItem
    {
        public string RelativePath { get; set; } = "";
        public bool ExistedBefore { get; set; }
    }
}
