using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Profiles;

public sealed class ProfileBundleService : IProfileBundleService
{
    private const int BundleSchemaVersion = 1;
    private const string GeneratedWithVersion = "0.3.8";
    private const int MaxEntries = 2048;
    private const long MaxManifestBytes = 2L * 1024L * 1024L;
    private const long MaxProfileBytes = 32L * 1024L * 1024L;
    private const long MaxPayloadBytes = 2L * 1024L * 1024L * 1024L;
    private const long MaxAggregateBytes = 8L * 1024L * 1024L * 1024L;

    private static readonly JsonSerializerOptions BundleJsonOptions = CreateBundleJsonOptions();

    private readonly IProfileService _profiles;
    private readonly IProfileHealthService _health;
    private readonly IPackageArtifactStore _artifacts;
    private readonly string _dataDirectory;

    public ProfileBundleService(
        IProfileService profiles,
        IProfileHealthService health,
        IPackageArtifactStore artifacts,
        string dataDirectory)
    {
        _profiles = profiles;
        _health = health;
        _artifacts = artifacts;
        _dataDirectory = dataDirectory;
    }

    public ProfileBundleExportPlan BuildExportPlan(ModProfile profile, IReadOnlyList<InstalledMod> installedMods)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(installedMods);

        var health = _health.Evaluate(profile, installedMods);
        var items = health.Items.Select(BuildExportItem).ToArray();
        return new ProfileBundleExportPlan
        {
            ProfileName = profile.Name,
            Items = items
        };
    }

    public ProfileOperationResult ExportBundle(
        ModProfile profile,
        IReadOnlyList<InstalledMod> installedMods,
        string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return ProfileOperationResult.Fail("Bundle destination is not available.");

        var finalPath = destinationPath.EndsWith(".htfbundle", StringComparison.OrdinalIgnoreCase)
            ? destinationPath
            : destinationPath + ".htfbundle";
        var tempBundle = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var staging = Path.Combine(_dataDirectory, "staging", "bundle-export-" + Guid.NewGuid().ToString("N"));
        var profilePath = Path.Combine(staging, "profile.htfprofile");

        try
        {
            Directory.CreateDirectory(staging);
            var portableResult = _profiles.ExportPortablePackage(profile, installedMods, profilePath);
            if (!portableResult.Success)
                return portableResult;

            var plan = BuildExportPlan(profile, installedMods);
            var profileHash = ComputeSha256(profilePath);
            var manifest = new HtfBundleManifest
            {
                SchemaVersion = BundleSchemaVersion,
                GeneratedWithVersion = GeneratedWithVersion,
                ProfileEntry = "profile.htfprofile",
                ProfileSha256 = profileHash
            };

            foreach (var item in plan.Items.Where(item => item.Disposition == ProfileBundleExportDisposition.Bundled))
            {
                var artifact = item.Artifact
                    ?? throw new InvalidOperationException("A bundled export item is missing its verified source artifact.");
                var requirement = item.Expectation.Requirement;
                var folder = HashText(requirement.PortableId).ToLowerInvariant();
                var entryName = $"payload/{folder}/{SafeFileName(artifact.FileName)}";
                manifest.Payloads.Add(new HtfBundlePayloadDescriptor
                {
                    PortableId = requirement.PortableId,
                    PackageKey = requirement.PackageKey,
                    IntrinsicId = requirement.IntrinsicId,
                    Version = requirement.Version,
                    Source = requirement.Source,
                    ArtifactKind = artifact.Kind,
                    Entry = entryName,
                    Sha256 = artifact.Sha256,
                    UncompressedSize = artifact.Length
                });
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(finalPath));
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            if (File.Exists(tempBundle)) File.Delete(tempBundle);

            using (var archive = ZipFile.Open(tempBundle, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("bundle.json", CompressionLevel.Optimal);
                using (var stream = manifestEntry.Open())
                    JsonSerializer.Serialize(stream, manifest, BundleJsonOptions);

                AddFileEntry(archive, manifest.ProfileEntry, profilePath, CompressionLevel.NoCompression);

                var byPortableId = plan.Items
                    .Where(item => item.Disposition == ProfileBundleExportDisposition.Bundled && item.Artifact is not null)
                    .ToDictionary(item => item.Expectation.Requirement.PortableId, StringComparer.OrdinalIgnoreCase);
                foreach (var payload in manifest.Payloads)
                {
                    var item = byPortableId[payload.PortableId];
                    AddFileEntry(
                        archive,
                        payload.Entry,
                        item.Artifact!.Path,
                        payload.ArtifactKind == HtfBundleArtifactKind.Archive ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                }
            }

            // Validate the produced archive before replacing the destination.
            var inspection = InspectBundle(tempBundle, installedMods);
            if (!inspection.IsValid)
                throw new InvalidDataException("Generated bundle failed validation: " + inspection.Error);

            File.Move(tempBundle, finalPath, true);
            return ProfileOperationResult.Ok("Portable profile bundle exported.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempBundle)) File.Delete(tempBundle); } catch { }
            return ProfileOperationResult.Fail(ex.Message);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public ProfileBundleInspection InspectBundle(string bundlePath, IReadOnlyList<InstalledMod> installedMods)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            return ProfileBundleInspection.Invalid(bundlePath, "Bundle does not exist.");

        string? tempProfile = null;
        try
        {
            using var archive = ZipFile.OpenRead(bundlePath);
            var validation = ValidateBundleArchive(archive, out var manifest, out var profileEntry);
            if (validation is not null || manifest is null || profileEntry is null)
                return ProfileBundleInspection.Invalid(bundlePath, validation ?? "Bundle manifest is invalid.");

            tempProfile = MaterializeProfileForRead(profileEntry, manifest.ProfileSha256);
            var profileInspection = _profiles.InspectPortablePackage(tempProfile, installedMods);
            if (!profileInspection.IsValid)
                return ProfileBundleInspection.Invalid(bundlePath, "Embedded profile is invalid: " + profileInspection.Error);

            var ephemeral = CreateEphemeralProfile(profileInspection);
            var health = _health.Evaluate(ephemeral, installedMods);
            var requirementsByPortableId = ephemeral.ExpectedMods.ToDictionary(
                item => item.Requirement.PortableId,
                item => item.Requirement,
                StringComparer.OrdinalIgnoreCase);

            foreach (var payload in manifest.Payloads)
            {
                if (!requirementsByPortableId.TryGetValue(payload.PortableId, out var requirement))
                    return ProfileBundleInspection.Invalid(bundlePath, $"Payload '{payload.PortableId}' does not map to the embedded profile.");
                var identityError = ValidatePayloadIdentity(payload, requirement);
                if (identityError is not null)
                    return ProfileBundleInspection.Invalid(bundlePath, identityError);
            }

            var payloadByPortableId = manifest.Payloads.ToDictionary(
                payload => payload.PortableId,
                StringComparer.OrdinalIgnoreCase);
            var items = health.Items.Select(item =>
            {
                payloadByPortableId.TryGetValue(item.Expectation.Requirement.PortableId, out var payload);
                return new ProfileBundleInspectionItem
                {
                    Health = item,
                    BundledPayload = item.Status is ProfileHealthStatus.Missing or ProfileHealthStatus.VersionMismatch ? payload : null
                };
            }).ToArray();

            return new ProfileBundleInspection
            {
                IsValid = true,
                BundlePath = bundlePath,
                Manifest = manifest,
                ProfileInspection = profileInspection,
                Health = health,
                Items = items
            };
        }
        catch (Exception ex)
        {
            return ProfileBundleInspection.Invalid(bundlePath, ex.Message);
        }
        finally
        {
            TryDeleteFile(tempProfile);
        }
    }

    public ProfileOperationResult ImportEmbeddedProfile(
        string bundlePath,
        IReadOnlyList<InstalledMod> installedMods,
        string? importName = null)
    {
        string? tempProfile = null;
        try
        {
            using var archive = ZipFile.OpenRead(bundlePath);
            var validation = ValidateBundleArchive(archive, out var manifest, out var profileEntry);
            if (validation is not null || manifest is null || profileEntry is null)
                return ProfileOperationResult.Fail(validation ?? "Bundle is invalid.");

            tempProfile = MaterializeProfileForRead(profileEntry, manifest.ProfileSha256);
            return _profiles.ImportPortablePackage(tempProfile, installedMods, importName);
        }
        catch (Exception ex)
        {
            return ProfileOperationResult.Fail(ex.Message);
        }
        finally
        {
            TryDeleteFile(tempProfile);
        }
    }

    public BundledPackageMaterialization MaterializePayload(
        string bundlePath,
        HtfBundlePayloadDescriptor descriptor,
        ProfileModRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(requirement);

        using var archive = ZipFile.OpenRead(bundlePath);
        var validation = ValidateBundleArchive(archive, out var manifest, out _);
        if (validation is not null || manifest is null)
            throw new InvalidDataException(validation ?? "Bundle is invalid.");

        var canonical = manifest.Payloads.FirstOrDefault(item =>
            item.PortableId.Equals(descriptor.PortableId, StringComparison.OrdinalIgnoreCase) &&
            NormalizeArchivePath(item.Entry).Equals(NormalizeArchivePath(descriptor.Entry), StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
            throw new InvalidDataException("The selected bundle payload is not present in the manifest.");

        var identityError = ValidatePayloadIdentity(canonical, requirement);
        if (identityError is not null)
            throw new InvalidDataException(identityError);

        var entry = FindUniqueEntry(archive, canonical.Entry)
            ?? throw new InvalidDataException("The selected bundle payload entry is missing.");
        if (entry.Length != canonical.UncompressedSize || entry.Length < 0 || entry.Length > MaxPayloadBytes)
            throw new InvalidDataException("The selected bundle payload size does not match the manifest.");

        var tempDirectory = Path.Combine(_dataDirectory, "staging", "bundle-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var extension = canonical.ArtifactKind == HtfBundleArtifactKind.Assembly ? ".dll" : ".zip";
        var outputPath = Path.Combine(tempDirectory, "package" + extension);

        try
        {
            using var input = entry.Open();
            using var output = File.Create(outputPath);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long written = 0;
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                written = checked(written + read);
                if (written > canonical.UncompressedSize || written > MaxPayloadBytes)
                    throw new InvalidDataException("The selected bundle payload expands beyond its declared size.");
                hash.AppendData(buffer, 0, read);
                output.Write(buffer, 0, read);
            }

            if (written != canonical.UncompressedSize)
                throw new InvalidDataException("The selected bundle payload size does not match the manifest.");
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!actualHash.Equals(canonical.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected bundle payload failed its SHA-256 integrity check.");

            return new BundledPackageMaterialization
            {
                SourcePath = outputPath,
                TemporaryDirectory = tempDirectory,
                Metadata = new ModInstallMetadata
                {
                    Source = requirement.Source,
                    PackageKey = requirement.PackageKey,
                    IntrinsicId = requirement.IntrinsicId,
                    Name = requirement.Name,
                    Version = requirement.Version,
                    Author = requirement.Author
                }
            };
        }
        catch
        {
            TryDeleteDirectory(tempDirectory);
            throw;
        }
    }

    private ProfileBundleExportItem BuildExportItem(ProfileHealthItem health)
    {
        var expectation = health.Expectation;
        var requirement = expectation.Requirement;
        if ((health.Status is ProfileHealthStatus.Missing or ProfileHealthStatus.VersionMismatch) &&
            expectation.MetadataQuality == ProfileExpectationMetadataQuality.Complete &&
            (!string.IsNullOrWhiteSpace(requirement.PackageKey) || !string.IsNullOrWhiteSpace(requirement.IntrinsicId)) &&
            NormalizeVersion(requirement.Version) != "—")
        {
            var expectedArtifact = _artifacts.FindExactArtifact(requirement);
            if (expectedArtifact is not null)
            {
                return new ProfileBundleExportItem
                {
                    Expectation = expectation,
                    Health = health,
                    InstalledMod = health.InstalledMod,
                    Disposition = ProfileBundleExportDisposition.Bundled,
                    Artifact = expectedArtifact,
                    Reason = health.Status == ProfileHealthStatus.VersionMismatch
                        ? "The exact profile-expected version is retained in artifact history; current installed version drift does not change the desired bundle state."
                        : "The expected Mod is currently missing, but its exact verified source artifact is retained in package history."
                };
            }
        }

        if (health.Status == ProfileHealthStatus.VersionMismatch)
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = health.InstalledMod,
                Disposition = ProfileBundleExportDisposition.VersionDrift,
                Reason = "Installed version differs from the profile expectation and no verified artifact for the exact expected version is available."
            };
        }

        if (health.Status == ProfileHealthStatus.IdentityUncertain ||
            expectation.MetadataQuality != ProfileExpectationMetadataQuality.Complete)
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = health.InstalledMod,
                Disposition = ProfileBundleExportDisposition.Manual,
                Reason = "The profile identity is not strong enough for automatic artifact sharing."
            };
        }

        if (health.Status == ProfileHealthStatus.Missing)
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                Disposition = requirement.Source == ModSourceType.Thunderstore
                    ? ProfileBundleExportDisposition.RemoteOnly
                    : ProfileBundleExportDisposition.Manual,
                Reason = requirement.Source == ModSourceType.Thunderstore
                    ? "The expected Mod is not installed, so no verified local artifact can be bundled."
                    : "The expected local Mod is not installed and has no verified source artifact."
            };
        }

        var installed = health.InstalledMod;
        if (string.IsNullOrWhiteSpace(requirement.PackageKey) && string.IsNullOrWhiteSpace(requirement.IntrinsicId))
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = installed,
                Disposition = requirement.Source == ModSourceType.Thunderstore
                    ? ProfileBundleExportDisposition.RemoteOnly
                    : ProfileBundleExportDisposition.Manual,
                Reason = "Full sharing requires a deterministic provider PackageKey or intrinsic Mod identity; filename/display-name matching alone is not bundle-safe."
            };
        }

        if (NormalizeVersion(requirement.Version) == "—")
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = installed,
                Disposition = requirement.Source == ModSourceType.Thunderstore
                    ? ProfileBundleExportDisposition.RemoteOnly
                    : ProfileBundleExportDisposition.Manual,
                Reason = "The profile does not record an exact expected version, so HTF Manager will not claim an exact bundled artifact."
            };
        }

        if (installed is not null && installed.Source != requirement.Source)
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = installed,
                Disposition = requirement.Source == ModSourceType.Thunderstore
                    ? ProfileBundleExportDisposition.RemoteOnly
                    : ProfileBundleExportDisposition.Manual,
                Reason = "The installed Mod source does not match the profile expectation, so its artifact is not bundled under a different provenance."
            };
        }

        if (installed is null || !installed.IsManaged || installed.Source is ModSourceType.External or ModSourceType.Development)
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = installed,
                Disposition = requirement.Source == ModSourceType.Thunderstore
                    ? ProfileBundleExportDisposition.RemoteOnly
                    : ProfileBundleExportDisposition.Manual,
                Reason = "Only verified HTF-managed source artifacts are automatically bundled."
            };
        }

        var artifact = _artifacts.FindVerifiedArtifact(installed);
        if (artifact is null)
        {
            return new ProfileBundleExportItem
            {
                Expectation = expectation,
                Health = health,
                InstalledMod = installed,
                Disposition = requirement.Source == ModSourceType.Thunderstore
                    ? ProfileBundleExportDisposition.RemoteOnly
                    : ProfileBundleExportDisposition.Manual,
                Reason = "The managed installation has no verified retained source artifact."
            };
        }

        return new ProfileBundleExportItem
        {
            Expectation = expectation,
            Health = health,
            InstalledMod = installed,
            Disposition = ProfileBundleExportDisposition.Bundled,
            Artifact = artifact,
            Reason = "Verified source artifact is available for full sharing."
        };
    }

    private string? ValidateBundleArchive(
        ZipArchive archive,
        out HtfBundleManifest? manifest,
        out ZipArchiveEntry? profileEntry)
    {
        manifest = null;
        profileEntry = null;
        if (archive.Entries.Count > MaxEntries) return "Bundle contains too many archive entries.";

        long aggregate = 0;
        foreach (var entry in archive.Entries)
        {
            if (!IsSafeArchivePath(entry.FullName)) return $"Bundle contains an unsafe archive path: {entry.FullName}";
            if (IsSymlink(entry)) return $"Bundle contains a symbolic-link entry: {entry.FullName}";
            if (entry.Length < 0) return "Bundle contains an invalid entry size.";
            aggregate = checked(aggregate + entry.Length);
            if (aggregate > MaxAggregateBytes) return "Bundle declared uncompressed size exceeds the supported limit.";
        }

        var bundleEntries = FindEntries(archive, "bundle.json");
        if (bundleEntries.Length != 1) return "Bundle must contain exactly one root bundle.json.";
        var manifestEntry = bundleEntries[0];
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaxManifestBytes)
            return "Bundle manifest is missing or too large.";

        try
        {
            var bytes = ReadEntryBytes(manifestEntry, MaxManifestBytes, "Bundle manifest");
            manifest = JsonSerializer.Deserialize<HtfBundleManifest>(bytes, BundleJsonOptions);
        }
        catch (JsonException)
        {
            return "Bundle manifest JSON is invalid.";
        }

        if (manifest is null) return "Bundle manifest could not be read.";
        if (manifest.SchemaVersion != BundleSchemaVersion) return "Unsupported bundle schema version.";
        if (string.IsNullOrWhiteSpace(manifest.GeneratedWithVersion) || manifest.GeneratedWithVersion.Length > 64)
            return "Bundle generator version is invalid.";
        if (string.IsNullOrWhiteSpace(manifest.ProfileEntry) || !IsSafeArchivePath(manifest.ProfileEntry))
            return "Bundle profile entry is invalid.";
        if (!NormalizeArchivePath(manifest.ProfileEntry).Equals("profile.htfprofile", StringComparison.OrdinalIgnoreCase))
            return "Bundle profile entry must be the root profile.htfprofile file.";
        if (string.IsNullOrWhiteSpace(manifest.ProfileSha256) || !IsSha256(manifest.ProfileSha256))
            return "Bundle profile SHA-256 is invalid.";

        var profileEntries = FindEntries(archive, manifest.ProfileEntry);
        if (profileEntries.Length != 1) return "Bundle must contain exactly one embedded profile entry.";
        profileEntry = profileEntries[0];
        if (profileEntry.Length <= 0 || profileEntry.Length > MaxProfileBytes)
            return "Embedded profile is empty or too large.";
        if (!NormalizeArchivePath(manifest.ProfileEntry).EndsWith(".htfprofile", StringComparison.OrdinalIgnoreCase))
            return "Bundle profile entry must use the .htfprofile extension.";

        if (manifest.Payloads.Count > 512) return "Bundle contains too many payload descriptors.";
        if (manifest.Payloads.Any(payload => string.IsNullOrWhiteSpace(payload.PortableId)))
            return "Bundle contains a payload without a portable Mod identifier.";
        if (manifest.Payloads.GroupBy(payload => payload.PortableId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return "Bundle contains duplicate portable Mod payload identifiers.";
        if (manifest.Payloads.GroupBy(payload => NormalizeArchivePath(payload.Entry), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return "Bundle contains duplicate payload entry mappings.";

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bundle.json",
            NormalizeArchivePath(manifest.ProfileEntry)
        };
        foreach (var payload in manifest.Payloads)
        {
            if (string.IsNullOrWhiteSpace(payload.PackageKey) && string.IsNullOrWhiteSpace(payload.IntrinsicId))
                return $"Bundle payload has no deterministic provider or intrinsic identity: {payload.PortableId}";
            if ((payload.PackageKey?.Length ?? 0) > 512 || (payload.IntrinsicId?.Length ?? 0) > 512)
                return $"Bundle payload identity is too long: {payload.PortableId}";
            if (!IsSafeArchivePath(payload.Entry) || !NormalizeArchivePath(payload.Entry).StartsWith("payload/", StringComparison.OrdinalIgnoreCase))
                return $"Bundle payload path is invalid: {payload.Entry}";
            if (!IsSha256(payload.Sha256)) return $"Bundle payload SHA-256 is invalid: {payload.PortableId}";
            if (payload.UncompressedSize < 0 || payload.UncompressedSize > MaxPayloadBytes)
                return $"Bundle payload size is invalid: {payload.PortableId}";

            var extension = Path.GetExtension(NormalizeArchivePath(payload.Entry));
            if (payload.ArtifactKind == HtfBundleArtifactKind.Archive && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return $"Archive payload must be a .zip file: {payload.PortableId}";
            if (payload.ArtifactKind == HtfBundleArtifactKind.Assembly && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                return $"Assembly payload must be a .dll file: {payload.PortableId}";

            var entries = FindEntries(archive, payload.Entry);
            if (entries.Length != 1) return $"Bundle payload entry is missing or duplicated: {payload.PortableId}";
            if (entries[0].Length != payload.UncompressedSize)
                return $"Bundle payload size does not match its manifest: {payload.PortableId}";
            referenced.Add(NormalizeArchivePath(payload.Entry));
        }

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            if (!referenced.Contains(NormalizeArchivePath(entry.FullName)))
                return $"Bundle contains an unreferenced file entry: {entry.FullName}";
        }

        return null;
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry, long maxBytes, string label)
    {
        using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, maxBytes));
        var buffer = new byte[64 * 1024];
        long written = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            written = checked(written + read);
            if (written > maxBytes)
                throw new InvalidDataException(label + " exceeds the supported size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private string MaterializeProfileForRead(ZipArchiveEntry entry, string expectedHash)
    {
        var directory = Path.Combine(_dataDirectory, "staging", "bundle-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "profile.htfprofile");
        try
        {
            using var input = entry.Open();
            using var output = File.Create(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long written = 0;
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                written = checked(written + read);
                if (written > MaxProfileBytes)
                    throw new InvalidDataException("Embedded profile exceeds the supported size limit.");
                hash.AppendData(buffer, 0, read);
                output.Write(buffer, 0, read);
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded profile failed its SHA-256 integrity check.");
            return path;
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private static ModProfile CreateEphemeralProfile(ProfilePackageInspection inspection)
    {
        var profile = new ModProfile { Name = inspection.ProfileName };
        foreach (var preview in inspection.Mods)
        {
            profile.ExpectedMods.Add(new ProfileModExpectation
            {
                Requirement = CloneRequirement(preview.Requirement),
                ResolvedModId = preview.MatchedInstalledModId,
                MetadataQuality = ProfileExpectationMetadataQuality.Complete
            });
            if (preview.Matched && !string.IsNullOrWhiteSpace(preview.MatchedInstalledModId))
                profile.ModStates[preview.MatchedInstalledModId!] = preview.Requirement.Enabled;
            else
                profile.UnresolvedMods.Add(CloneRequirement(preview.Requirement));
        }
        return profile;
    }

    private static string? ValidatePayloadIdentity(HtfBundlePayloadDescriptor payload, ProfileModRequirement requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement.PackageKey) && string.IsNullOrWhiteSpace(requirement.IntrinsicId))
            return $"Bundle payload requirement has no deterministic provider or intrinsic identity: {requirement.Name}";
        if (!payload.PortableId.Equals(requirement.PortableId, StringComparison.OrdinalIgnoreCase))
            return "Bundle payload portable identity does not match the embedded profile.";
        if (!SameOptional(payload.PackageKey, requirement.PackageKey))
            return $"Bundle payload PackageKey does not match the profile requirement: {requirement.Name}";
        if (!SameOptional(payload.IntrinsicId, requirement.IntrinsicId))
            return $"Bundle payload intrinsic identity does not match the profile requirement: {requirement.Name}";
        if (!VersionsEqual(payload.Version, requirement.Version))
            return $"Bundle payload version does not match the profile requirement: {requirement.Name}";
        if (payload.Source != requirement.Source)
            return $"Bundle payload source does not match the profile requirement: {requirement.Name}";
        return null;
    }

    private static ProfileModRequirement CloneRequirement(ProfileModRequirement source)
        => new()
        {
            PortableId = source.PortableId,
            Name = source.Name,
            Version = source.Version,
            Author = source.Author,
            PackageKey = source.PackageKey,
            IntrinsicId = source.IntrinsicId,
            FileName = source.FileName,
            Source = source.Source,
            Loader = source.Loader,
            Component = source.Component,
            Enabled = source.Enabled
        };

    private static bool SameOptional(string? left, string? right)
        => string.Equals(left?.Trim() ?? "", right?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool VersionsEqual(string? left, string? right)
        => string.Equals(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string? version)
        => string.IsNullOrWhiteSpace(version) ? "—" : version.Trim();

    private static void AddFileEntry(ZipArchive archive, string entryName, string sourcePath, CompressionLevel compression)
    {
        var entry = archive.CreateEntry(entryName, compression);
        using var input = File.OpenRead(sourcePath);
        using var output = entry.Open();
        input.CopyTo(output);
    }

    private static ZipArchiveEntry[] FindEntries(ZipArchive archive, string path)
    {
        var normalized = NormalizeArchivePath(path);
        return archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                            NormalizeArchivePath(entry.FullName).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static ZipArchiveEntry? FindUniqueEntry(ZipArchive archive, string path)
    {
        var entries = FindEntries(archive, path);
        return entries.Length == 1 ? entries[0] : null;
    }

    private static bool IsSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith('/') || path.StartsWith('\\')) return false;
        if (Path.IsPathRooted(path)) return false;
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') return false;

        var normalized = NormalizeArchivePath(path);
        if (normalized.Contains(':')) return false;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(segment => segment != ".." && segment != ".");
    }

    private static bool IsSymlink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixMode == 0xA000;
    }

    private static string NormalizeArchivePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "package.zip" : safe;
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var directory = Path.GetDirectoryName(path);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        TryDeleteDirectory(directory);
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private static JsonSerializerOptions CreateBundleJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
