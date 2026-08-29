using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IProfileRestoreService
{
    ProfileRestorePlan BuildPlan(ModProfile profile, IReadOnlyList<RemoteModPackage> catalog);
}
