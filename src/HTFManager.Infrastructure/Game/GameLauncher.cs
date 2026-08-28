using System.Diagnostics;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Game;

public sealed class GameLauncher : IGameLauncher
{
    private const string SteamUri = "steam://rungameid/4001890";

    public bool IsRunning(GameEnvironmentInfo environment)
    {
        if (environment.ExecutablePath is null)
            return false;

        var name = Path.GetFileNameWithoutExtension(environment.ExecutablePath);
        try { return Process.GetProcessesByName(name).Length > 0; }
        catch { return false; }
    }

    public void Launch(GameEnvironmentInfo environment)
    {
        if (!environment.GameFound || environment.ExecutablePath is null)
            throw new InvalidOperationException("How to Fish installation was not found.");

        try
        {
            Process.Start(new ProcessStartInfo(SteamUri) { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo(environment.ExecutablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = environment.GameDirectory
            });
        }
    }
}
