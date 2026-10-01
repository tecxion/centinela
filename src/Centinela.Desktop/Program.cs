using Avalonia;
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
            App.Platform = PlatformServices.Create(Environment.ProcessPath!);
            // The running instance shows its window; this one has nothing to do.
            if (!App.Platform.SingleInstance.TryClaim()) return;
        }
        catch (Exception ex)
        {
            App.StartupError = ex.Message;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
