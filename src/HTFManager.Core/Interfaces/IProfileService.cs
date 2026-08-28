using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IProfileService
{
    IReadOnlyList<ModProfile> LoadProfiles();
    ModProfile Capture(string name, IReadOnlyList<InstalledMod> mods);
    void Save(ModProfile profile);
    void Delete(ModProfile profile);
    ProfilePackageInspection InspectPortablePackage(string packagePath, IReadOnlyList<InstalledMod> installedMods);
    ProfileOperationResult ExportPortablePackage(
        ModProfile profile,
        IReadOnlyList<InstalledMod> installedMods,
        string destinationPath);
    ProfileOperationResult ImportPortablePackage(
        string packagePath,
        IReadOnlyList<InstalledMod> installedMods,
        string? importName = null);
    ProfileOperationResult ResolveMissingMods(ModProfile profile, IReadOnlyList<InstalledMod> installedMods);
    void RemoveMissingMod(ModProfile profile, string portableId);
    ProfileOperationResult CaptureConfigurationSnapshots(
        ModProfile profile,
        IReadOnlyList<ModConfigurationDocument> configurations,
        string? gameDirectory);
    ProfileOperationResult ClearConfigurationSnapshots(ModProfile profile);
    ProfileOperationResult Apply(
        ModProfile profile,
        IReadOnlyList<InstalledMod> mods,
        IModService modService,
        string? gameDirectory);
}
