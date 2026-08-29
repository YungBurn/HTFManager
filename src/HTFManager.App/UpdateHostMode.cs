using System.Diagnostics;
using System.Security.Cryptography;

namespace HTFManager.App;

internal static class UpdateHostMode
{
    private const string AckPrefix = "HTFManager.UpdateAck.";
    private static string? _pendingStartupAcknowledgement;

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
            var expectedSize = long.Parse(GetValue(args, "--size") ?? throw new ArgumentException("Missing update size."));

            if (expectedSize <= 0) throw new InvalidDataException("Update size must be greater than zero.");
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Update SHA-256 is invalid.");

            WaitForParentExit(parentPid);
            if (!File.Exists(staged)) throw new FileNotFoundException("Staged update is missing.", staged);
            if (new FileInfo(staged).Length != expectedSize)
                throw new InvalidDataException("Staged update failed size verification.");
            var actualHash = ComputeSha256(staged);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged update failed SHA-256 verification.");

            var targetDirectory = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Update target directory is unavailable.");
            Directory.CreateDirectory(targetDirectory);
            var backup = target + ".old";
            var acknowledgement = Path.Combine(Path.GetTempPath(), $"{AckPrefix}{Guid.NewGuid():N}.ready");
            TryDelete(acknowledgement);
            TryDelete(backup);

            var movedOriginal = false;
            Process? updatedProcess = null;
            try
            {
                if (File.Exists(target))
                {
                    File.Move(target, backup, true);
                    movedOriginal = true;
                }

                File.Copy(staged, target, true);
                updatedProcess = TryStartTarget(target, targetDirectory, acknowledgement);
                if (updatedProcess is null)
                    throw new InvalidOperationException("The updated HTF Manager executable could not be started.");

                if (!WaitForStartupAcknowledgement(updatedProcess, acknowledgement, TimeSpan.FromSeconds(30)))
                    throw new InvalidOperationException("The updated HTF Manager executable did not confirm a successful startup.");

                updatedProcess.Dispose();
                updatedProcess = null;
                TryDelete(acknowledgement);
                TryDelete(backup);
                TryDelete(staged);
                TryDeleteDirectory(Path.GetDirectoryName(staged));
                exitCode = 0;
                return true;
            }
            catch
            {
                StopProcess(updatedProcess);
                TryDelete(acknowledgement);
                try
                {
                    TryDelete(target);
                    if (movedOriginal && File.Exists(backup))
                    {
                        File.Move(backup, target, true);
                        _ = TryStartTarget(target, targetDirectory, null);
                    }
                    TryDelete(staged);
                    TryDeleteDirectory(Path.GetDirectoryName(staged));
                }
                catch
                {
                    // The original exception remains the update-host failure result.
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

    public static string[] PrepareApplicationArguments(string[] args)
    {
        _pendingStartupAcknowledgement = null;
        var acknowledgement = GetValue(args, "--update-ack");
        if (!string.IsNullOrWhiteSpace(acknowledgement))
        {
            var fullPath = Path.GetFullPath(acknowledgement);
            if (IsExpectedAcknowledgementPath(fullPath))
                _pendingStartupAcknowledgement = fullPath;
        }

        return RemoveNamedValue(args, "--update-ack");
    }

    public static void SignalStartupReady()
    {
        var acknowledgement = _pendingStartupAcknowledgement;
        _pendingStartupAcknowledgement = null;
        if (string.IsNullOrWhiteSpace(acknowledgement) || !IsExpectedAcknowledgementPath(acknowledgement))
            return;

        try
        {
            File.WriteAllText(acknowledgement, $"ready:{Environment.ProcessId}:{DateTimeOffset.UtcNow:O}");
        }
        catch
        {
            // Failure to acknowledge causes the update host to roll back after its timeout.
        }
    }

    public static void CleanupStaleHosts()
    {
        CleanupTempFiles("HTFManager.UpdateHost.*.exe", TimeSpan.FromMinutes(1), skipCurrentProcess: true);
        CleanupTempFiles($"{AckPrefix}*.ready", TimeSpan.FromHours(1), skipCurrentProcess: false);
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

    private static string[] RemoveNamedValue(IReadOnlyList<string> args, string name)
    {
        var result = new List<string>(args.Count);
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < args.Count) index++;
                continue;
            }
            result.Add(args[index]);
        }
        return result.ToArray();
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

    private static bool WaitForStartupAcknowledgement(Process process, string acknowledgement, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (File.Exists(acknowledgement)) return true;
            try
            {
                if (process.HasExited) return false;
            }
            catch
            {
                return false;
            }
            Thread.Sleep(100);
        }
        return File.Exists(acknowledgement);
    }

    private static Process? TryStartTarget(string target, string targetDirectory, string? acknowledgement)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = targetDirectory,
                UseShellExecute = false
            };
            if (!string.IsNullOrWhiteSpace(acknowledgement))
            {
                startInfo.ArgumentList.Add("--update-ack");
                startInfo.ArgumentList.Add(acknowledgement);
            }
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static bool IsExpectedAcknowledgementPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var temp = Path.GetFullPath(Path.GetTempPath());
            var fileName = Path.GetFileName(fullPath);
            return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase) &&
                   fileName.StartsWith(AckPrefix, StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".ready", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupTempFiles(string pattern, TimeSpan minimumAge, bool skipCurrentProcess)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), pattern))
            {
                try
                {
                    if (skipCurrentProcess && string.Equals(Path.GetFullPath(path), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > minimumAge)
                        File.Delete(path);
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
