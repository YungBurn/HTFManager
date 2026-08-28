using HTFManager.Core.Models;

namespace HTFManager.Core.Interfaces;

public interface IGameLauncher
{
    bool IsRunning(GameEnvironmentInfo environment);
    void Launch(GameEnvironmentInfo environment);
}
