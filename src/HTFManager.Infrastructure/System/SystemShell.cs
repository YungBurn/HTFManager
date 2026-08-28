using System.Diagnostics;
using HTFManager.Core.Interfaces;

namespace HTFManager.Infrastructure.System;

public sealed class SystemShell : ISystemShell
{
    public void OpenPath(string path)
    {
        if (!Directory.Exists(path)) return;
        Open(path);
    }

    public void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        Open(path);
    }

    public void OpenUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out _)) return;
        Open(uri);
    }

    private static void Open(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return;
        }

        var command = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        Process.Start(new ProcessStartInfo(command, $"\"{target}\"") { UseShellExecute = false });
    }
}
