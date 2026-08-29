using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IProfileBundleService
{
    ProfileBundleExportPlan BuildExportPlan(ModProfile profile, IReadOnlyList<InstalledMod> installedMods);
    ProfileOperationResult ExportBundle(
        ModProfile profile,
        IReadOnlyList<InstalledMod> installedMods,
        string destinationPath);
    ProfileBundleInspection InspectBundle(string bundlePath, IReadOnlyList<InstalledMod> installedMods);
    ProfileOperationResult ImportEmbeddedProfile(
        string bundlePath,
        IReadOnlyList<InstalledMod> installedMods,
        string? importName = null);
    BundledPackageMaterialization MaterializePayload(
        string bundlePath,
        HtfBundlePayloadDescriptor descriptor,
        ProfileModRequirement requirement);
}
