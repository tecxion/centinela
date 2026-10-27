using Avalonia;
using Centinela.Core.Platform;
using Centinela.Desktop.Services.Ui;
using Centinela.Platform;

namespace Centinela.Desktop;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Claimed before Avalonia starts: a shutdown requested before its loop runs is ignored.
        try
        {
            App.Platform = PlatformServices.Create(Environment.ProcessPath!, UiThread.Context);
            // The running instance shows its window; this one has nothing to do.
            if (!App.Platform.SingleInstance.TryClaim()) return;
            // Asked once, while Avalonia starts: the answer may wait for the session bus.
            if (App.Platform.TrayAvailability is { } trayCheck) App.TrayAvailable = Task.Run(() => IsTrayAvailable(trayCheck));
        }
        catch (Exception ex)
        {
            App.StartupError = ex.Message;
        }
        // Closing the window may only hide it in the tray: the app ends when App says so («Salir», session end).
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    static bool IsTrayAvailable(ITrayAvailability check)
    {
        try { return check.IsAvailable; }
        catch (Exception) { return false; }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
