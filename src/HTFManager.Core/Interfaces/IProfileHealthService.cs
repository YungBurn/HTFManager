using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IProfileHealthService
{
    ProfileHealthReport Evaluate(ModProfile profile, IReadOnlyList<InstalledMod> installedMods);
}
