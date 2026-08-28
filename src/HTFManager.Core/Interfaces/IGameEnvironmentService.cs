using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IGameEnvironmentService
{
    GameEnvironmentInfo Inspect(string? gameDirectory);
    IReadOnlyList<DiagnosticItem> Diagnose(GameEnvironmentInfo environment);
}
