using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IProfileVersionReconciliationService
{
    ProfileVersionReconciliationPlan BuildPlan(
        ModProfile profile,
        IReadOnlyList<InstalledMod> installedMods,
        IReadOnlyList<RemoteModPackage> catalog,
        IReadOnlyList<HtfBundlePayloadDescriptor>? bundledPayloads = null,
        bool catalogAvailable = true);
}
