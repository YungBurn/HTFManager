using System.Text.RegularExpressions;
using HTFManager.Core.Interfaces;
using Microsoft.Win32;

namespace HTFManager.Infrastructure.Game;

public sealed partial class SteamGameLocator : IGameLocator
{
    private const string AppId = "4001890";
    private const string ExeName = "How to Fish.exe";

    public string? LocateGameDirectory(string? preferredPath = null)
    {
        var preferred = NormalizeGameDirectory(preferredPath);
        if (preferred is not null)
            return preferred;

        foreach (var steamRoot in GetSteamRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var library in GetLibraries(steamRoot).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var steamApps = Path.Combine(library, "steamapps");
                var manifest = Path.Combine(steamApps, $"appmanifest_{AppId}.acf");
                if (!File.Exists(manifest))
                    continue;

                var installDir = ReadInstallDir(manifest) ?? "How to Fish";
                var root = Path.Combine(steamApps, "common", installDir);
                var normalized = NormalizeGameDirectory(root);
                if (normalized is not null)
                    return normalized;
            }
        }

        return null;
    }

    private static string? NormalizeGameDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        var direct = Path.Combine(path, ExeName);
        if (File.Exists(direct))
            return Path.GetFullPath(path);

        var nested = Path.Combine(path, "How to Fish", ExeName);
        if (File.Exists(nested))
            return Path.GetFullPath(Path.GetDirectoryName(nested)!);

        try
        {
            foreach (var child in Directory.EnumerateDirectories(path))
            {
                var candidate = Path.Combine(child, ExeName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(child);
            }
        }
        catch { }

        return null;
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            string? registrySteamPath = null;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                registrySteamPath = key?.GetValue("SteamPath") as string;
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(registrySteamPath))
                yield return registrySteamPath.Replace('/', Path.DirectorySeparatorChar);

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                yield return Path.Combine(programFilesX86, "Steam");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
        }
    }

    private static IEnumerable<string> GetLibraries(string steamRoot)
    {
        if (!Directory.Exists(steamRoot))
            yield break;

        yield return steamRoot;

        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file))
            yield break;

        string? text = null;
        try { text = File.ReadAllText(file); }
        catch { }
        if (text is null)
            yield break;

        foreach (Match match in LibraryPathRegex().Matches(text))
        {
            var path = match.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path))
                yield return path;
        }
    }

    private static string? ReadInstallDir(string manifest)
    {
        try
        {
            var match = InstallDirRegex().Match(File.ReadAllText(manifest));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch { return null; }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();
}
