using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using HTFManager.Core.Interfaces;
using HTFManager.Core.Models;

namespace HTFManager.Infrastructure.Updates;

public sealed class WindowsApplicationUpdateApplier : IApplicationUpdateApplier
{
    public bool CanApply(ApplicationUpdateInfo update, out string? reason)
    {
        reason = null;
        if (!OperatingSystem.IsWindows())
        {
            reason = "Automatic update replacement is currently supported only on Windows.";
            return false;
        }
        if (update.State != ApplicationUpdateState.Ready || update.Manifest is null || string.IsNullOrWhiteSpace(update.StagedPath) || !File.Exists(update.StagedPath))
        {
            reason = "The update has not been downloaded and verified.";
            return false;
        }
        if (!Version.TryParse(NormalizeVersion(update.CurrentVersion), out var currentVersion) ||
            !Version.TryParse(NormalizeVersion(update.LatestVersion), out var latestVersion) ||
            latestVersion.CompareTo(currentVersion) <= 0)
        {
            reason = "The staged application version is not newer than the running version.";
            return false;
        }
        if (!NormalizeVersion(update.Manifest.Version).Equals(NormalizeVersion(update.LatestVersion), StringComparison.OrdinalIgnoreCase))
        {
            reason = "The staged update manifest version does not match the selected release.";
            return false;
        }
        if (!string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location))
        {
            reason = "Restart-and-update is available only from the published single-file HTF Manager executable.";
            return false;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            reason = "The running executable path is unavailable.";
            return false;
        }

        var directory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(directory) || !CanWriteDirectory(directory))
        {
            reason = "HTF Manager cannot write to its current executable directory. Download the release manually or move HTF Manager to a user-writable folder.";
            return false;
        }

        var staged = Path.GetFullPath(update.StagedPath);
        if (Path.GetFullPath(processPath).Equals(staged, StringComparison.OrdinalIgnoreCase))
        {
            reason = "The staged update cannot be the running executable itself.";
            return false;
        }

        if (update.Manifest.Size <= 0 || new FileInfo(staged).Length != update.Manifest.Size)
        {
            reason = "The staged update no longer matches the expected file size.";
            return false;
        }

        return true;
    }

    public bool StartApplyAndRestart(ApplicationUpdateInfo update, out string? error)
    {
        error = null;
        if (!CanApply(update, out error)) return false;

        try
        {
            var processPath = Path.GetFullPath(Environment.ProcessPath!);
            var staged = Path.GetFullPath(update.StagedPath!);
            var manifest = update.Manifest!;

            if (new FileInfo(staged).Length != manifest.Size)
            {
                error = "The staged update no longer matches its manifest size.";
                return false;
            }

            var actualHash = ComputeSha256(staged);
            if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "The staged update no longer matches its SHA-256 manifest.";
                return false;
            }

            var hostPath = Path.Combine(Path.GetTempPath(), $"HTFManager.UpdateHost.{Guid.NewGuid():N}.exe");
            File.Copy(processPath, hostPath, true);

            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--apply-update");
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(processPath);
            startInfo.ArgumentList.Add("--staged");
            startInfo.ArgumentList.Add(staged);
            startInfo.ArgumentList.Add("--sha256");
            startInfo.ArgumentList.Add(manifest.Sha256);
            startInfo.ArgumentList.Add("--size");
            startInfo.ArgumentList.Add(manifest.Size.ToString());

            if (Process.Start(startInfo) is null)
            {
                TryDelete(hostPath);
                error = "Failed to start the update host.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string NormalizeVersion(string? value)
    {
        var normalized = (value ?? "0.0.0").Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        return normalized;
    }

    private static bool CanWriteDirectory(string directory)
    {
        var probe = Path.Combine(directory, $".htf-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
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
}
