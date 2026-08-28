using System.Diagnostics;
using System.Reflection;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Game;

public sealed class GameEnvironmentService : IGameEnvironmentService
{
    public GameEnvironmentInfo Inspect(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return new GameEnvironmentInfo();

        var exe = Path.Combine(gameDirectory, "How to Fish.exe");

        var bepRoot = Path.Combine(gameDirectory, "BepInEx");
        var bepCore = Path.Combine(bepRoot, "core");
        var bepPlugins = Path.Combine(bepRoot, "plugins");
        var bepConfig = Path.Combine(bepRoot, "config");
        var bepPatchers = Path.Combine(bepRoot, "patchers");
        var bepLog = Path.Combine(bepRoot, "LogOutput.log");
        var bepBootstrap = Path.Combine(gameDirectory, "winhttp.dll");
        var bepDll = Path.Combine(bepCore, "BepInEx.dll");
        var bepInstalled = Directory.Exists(bepCore) && File.Exists(bepBootstrap);

        var melonRoot = Path.Combine(gameDirectory, "MelonLoader");
        var melonMods = Path.Combine(gameDirectory, "Mods");
        var melonPlugins = Path.Combine(gameDirectory, "Plugins");
        var melonUserData = Path.Combine(gameDirectory, "UserData");
        var melonConfig = Path.Combine(melonUserData, "Loader.cfg");
        var melonLogs = Path.Combine(melonRoot, "Logs");
        var melonProxy = FindMelonProxy(gameDirectory, bepInstalled);
        var melonDobby = Path.Combine(gameDirectory, "dobby.dll");
        var melonDetected = Directory.Exists(melonRoot) || melonProxy is not null || File.Exists(melonDobby) ||
                            Directory.Exists(melonMods) || Directory.Exists(melonPlugins);
        var melonInstalled = Directory.Exists(melonRoot) && melonProxy is not null && File.Exists(melonDobby);
        var melonAssembly = FindMelonLoaderAssembly(melonRoot);

        return new GameEnvironmentInfo
        {
            GameDirectory = gameDirectory,
            ExecutablePath = exe,
            GameFound = File.Exists(exe),
            GameVersion = ReadFileVersion(exe),
            BepInEx = new BepInExEnvironmentInfo
            {
                Installed = bepInstalled,
                Healthy = bepInstalled && File.Exists(bepDll),
                Version = ReadAssemblyVersion(bepDll),
                RootDirectory = bepRoot,
                PluginsDirectory = bepPlugins,
                ConfigDirectory = bepConfig,
                PatchersDirectory = bepPatchers,
                LogPath = bepLog,
                BootstrapPath = bepBootstrap
            },
            MelonLoader = new MelonLoaderEnvironmentInfo
            {
                Detected = melonDetected,
                Installed = melonInstalled,
                Healthy = melonInstalled,
                Version = ReadAssemblyVersion(melonAssembly),
                RootDirectory = melonRoot,
                ModsDirectory = melonMods,
                PluginsDirectory = melonPlugins,
                UserDataDirectory = melonUserData,
                LoaderConfigPath = melonConfig,
                LogsDirectory = melonLogs,
                ProxyPath = melonProxy,
                DobbyPath = melonDobby
            }
        };
    }

    public IReadOnlyList<DiagnosticItem> Diagnose(GameEnvironmentInfo environment)
    {
        var root = environment.GameDirectory ?? "";
        var items = new List<DiagnosticItem>
        {
            new("How to Fish.exe", environment.GameFound, environment.ExecutablePath ?? "Not found")
        };

        if (environment.BepInEx.Installed || Directory.Exists(Path.Combine(root, "BepInEx")))
        {
            items.Add(new DiagnosticItem("BepInEx / winhttp.dll", environment.BepInEx.Installed, environment.BepInEx.BootstrapPath ?? "Not found"));
            items.Add(new DiagnosticItem("BepInEx/core", Directory.Exists(Path.Combine(root, "BepInEx", "core")), Path.Combine(root, "BepInEx", "core")));
            items.Add(new DiagnosticItem("BepInEx/plugins", Directory.Exists(environment.BepInEx.PluginsDirectory), environment.BepInEx.PluginsDirectory ?? "Not found"));
            items.Add(new DiagnosticItem("BepInEx LogOutput.log", File.Exists(environment.BepInEx.LogPath), environment.BepInEx.LogPath ?? "Not found"));
        }

        if (environment.MelonLoader.Detected)
        {
            items.Add(new DiagnosticItem("MelonLoader proxy", File.Exists(environment.MelonLoader.ProxyPath), environment.MelonLoader.ProxyPath ?? "Not found"));
            items.Add(new DiagnosticItem("MelonLoader / dobby.dll", File.Exists(environment.MelonLoader.DobbyPath), environment.MelonLoader.DobbyPath ?? "Not found"));
            items.Add(new DiagnosticItem("MelonLoader", Directory.Exists(environment.MelonLoader.RootDirectory), environment.MelonLoader.RootDirectory ?? "Not found"));
            items.Add(new DiagnosticItem("MelonLoader Mods", Directory.Exists(environment.MelonLoader.ModsDirectory), environment.MelonLoader.ModsDirectory ?? "Not found"));
        }

        return items;
    }

    private static string? FindMelonProxy(string gameDirectory, bool bepInExInstalled)
    {
        var names = new[]
        {
            "version.dll", "winmm.dll", "dinput.dll", "dinput8.dll", "dsound.dll",
            "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll",
            "ddraw.dll", "msacm32.dll"
        };

        foreach (var name in names)
        {
            var path = Path.Combine(gameDirectory, name);
            if (File.Exists(path)) return path;
        }

        if (!bepInExInstalled)
        {
            var winHttp = Path.Combine(gameDirectory, "winhttp.dll");
            if (File.Exists(winHttp)) return winHttp;
        }

        return null;
    }

    private static string? FindMelonLoaderAssembly(string melonRoot)
    {
        try
        {
            if (!Directory.Exists(melonRoot)) return null;
            return Directory.EnumerateFiles(melonRoot, "MelonLoader.dll", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string ReadFileVersion(string path)
    {
        try
        {
            if (!File.Exists(path)) return "—";
            return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "—";
        }
        catch { return "—"; }
    }

    private static string ReadAssemblyVersion(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "—";
            return AssemblyName.GetAssemblyName(path).Version?.ToString() ?? "—";
        }
        catch { return "—"; }
    }
}
