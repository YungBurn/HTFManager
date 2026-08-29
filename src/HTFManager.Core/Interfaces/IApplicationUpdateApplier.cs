using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IApplicationUpdateApplier
{
    bool CanApply(ApplicationUpdateInfo update, out string? reason);
    bool StartApplyAndRestart(ApplicationUpdateInfo update, out string? error);
}
