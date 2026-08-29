using System.Diagnostics;
using System.Security.Cryptography;

namespace HTFManager.App;

internal static class UpdateHostMode
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Any(argument => argument.Equals("--apply-update", StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            var parentPid = int.Parse(GetValue(args, "--parent-pid") ?? throw new ArgumentException("Missing parent PID."));
            var target = Path.GetFullPath(GetValue(args, "--target") ?? throw new ArgumentException("Missing update target."));
            var staged = Path.GetFullPath(GetValue(args, "--staged") ?? throw new ArgumentException("Missing staged update."));
            var expectedHash = GetValue(args, "--sha256") ?? throw new ArgumentException("Missing update SHA-256.");

            WaitForParentExit(parentPid);
            if (!File.Exists(staged)) throw new FileNotFoundException("Staged update is missing.", staged);
            var actualHash = ComputeSha256(staged);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged update failed SHA-256 verification.");

            var targetDirectory = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Update target directory is unavailable.");
            Directory.CreateDirectory(targetDirectory);
            var backup = target + ".old";
            TryDelete(backup);

            var movedOriginal = false;
            try
            {
                if (File.Exists(target))
                {
                    File.Move(target, backup, true);
                    movedOriginal = true;
                }

                File.Copy(staged, target, true);
                if (!TryStartTarget(target, targetDirectory))
                    throw new InvalidOperationException("The updated HTF Manager executable could not be started.");

                TryDelete(backup);
                TryDelete(staged);
                TryDeleteDirectory(Path.GetDirectoryName(staged));
                exitCode = 0;
                return true;
            }
            catch
            {
                try
                {
                    TryDelete(target);
                    if (movedOriginal && File.Exists(backup))
                    {
                        File.Move(backup, target, true);
                        _ = TryStartTarget(target, targetDirectory);
                    }
                }
                catch
                {
                }
                throw;
            }
        }
        catch
        {
            exitCode = 1;
            return true;
        }
    }

    public static void CleanupStaleHosts()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), "HTFManager.UpdateHost.*.exe"))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(path), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                    if (age > TimeSpan.FromMinutes(1)) File.Delete(path);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string? GetValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return null;
    }

    private static void WaitForParentExit(int parentPid)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (!parent.WaitForExit(60_000))
                throw new TimeoutException("HTF Manager did not exit before the update timeout.");
        }
        catch (ArgumentException)
        {
            // Parent already exited.
        }
    }


    private static bool TryStartTarget(string target, string targetDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = targetDirectory,
                UseShellExecute = true
            };
            return Process.Start(startInfo) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch
        {
        }
    }
}
