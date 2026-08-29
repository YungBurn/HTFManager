using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IProfileRestoreService
{
    ProfileRestorePlan BuildPlan(ModProfile profile, IReadOnlyList<RemoteModPackage> catalog);
    ProfileRestorePlan BuildPlan(
        ModProfile profile,
        IReadOnlyList<RemoteModPackage> catalog,
        IReadOnlyList<HtfBundlePayloadDescriptor> bundledPayloads,
        bool catalogAvailable = true);
}
