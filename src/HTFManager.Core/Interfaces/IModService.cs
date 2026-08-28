using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IModService
{
    IReadOnlyList<InstalledMod> Scan(GameEnvironmentInfo environment);
    bool SetEnabled(InstalledMod mod, bool enabled);
}
