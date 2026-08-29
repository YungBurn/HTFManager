using Avalonia;

namespace HTFManager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (UpdateHostMode.TryRun(args, out var exitCode))
        {
            Environment.ExitCode = exitCode;
            return;
        }

        var applicationArgs = UpdateHostMode.PrepareApplicationArguments(args);
        UpdateHostMode.CleanupStaleHosts();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(applicationArgs);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
