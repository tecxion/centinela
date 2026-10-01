using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Centinela.Core;
using Centinela.Desktop.Services.Ui;
using Centinela.Desktop.Views;
using Centinela.Media;
using Centinela.Platform;

namespace Centinela.Desktop;

public partial class App : Application
{
    internal static PlatformServices Platform { get; set; } = null!;

    /// <summary>
    /// Whether Avalonia's tray icon can show (Linux, macOS), asked once in Program.Main; false where the system has
    /// no such check (Windows has its own tray).
    /// </summary>
    internal static Task<bool> TrayAvailable { get; set; } = Task.FromResult(false);

    /// <summary>Why the system pieces could not be created in Program.Main; shown once the UI runs.</summary>
    internal static string? StartupError { get; set; }

    // The main window, also while it is hidden in the tray (then it is not the lifetime's MainWindow).
    static MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) Start(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        HandleUnexpectedErrors(desktop);
        if (StartupError is not null)
        {
            Fail(desktop, StartupError);
            return;
        }
        try
        {
            FFmpegLoader.Initialize();
        }
        catch (Exception ex)
        {
            Fail(desktop, ex.Message);
            return;
        }
        // The CamaraWin rename only ever happened on Windows.
        if (OperatingSystem.IsWindows() && !AppPaths.IsDataDirectoryOverridden)
        {
            try { LegacyData.Migrate(LegacyData.Directory, AppPaths.DataDirectory); }
            catch (Exception) { /* start empty; the user can import a copy */ }
        }
        try
        {
            var window = new MainWindow(startHidden: desktop.Args?.Contains("--tray") == true);
            _window = window;
            // The lifetime shows its MainWindow when its loop starts: started in the tray, it is left unset.
            if (!window.IsHiddenInTray) desktop.MainWindow = window;
            var sessionEnding = false;
            // «Salir»: once the window has stopped everything, the app ends.
            window.Closed += (_, _) =>
            {
                if (!sessionEnding) desktop.Shutdown();
            };
            // The session ends (or the system asks the app to quit): a real exit. The window is closed here, also
            // when it was never shown, so its wait runs; the lifetime then ends the app itself.
            desktop.ShutdownRequested += (_, _) =>
            {
                sessionEnding = true;
                window.ExitForShutdown();
            };
            Platform.SingleInstance.ListenForActivation(() => UiThread.Post(window.ShowFromTray));
        }
        catch (Exception ex)
        {
            Fail(desktop, $"No se pudo abrir Centinela: {ex.Message}");
        }
    }

    /// <summary>
    /// As in the Windows version: an exception nobody caught on the UI thread is shown and Centinela keeps running;
    /// an unobserved task exception is dropped silently.
    /// </summary>
    static void HandleUnexpectedErrors(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            var owner = _window is { IsVisible: true } window ? window : null;
            _ = MessageDialog.ShowAsync(owner, e.Exception.Message);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
    }

    /// <summary>Shows why Centinela cannot start and ends the process once the message is closed.</summary>
    static void Fail(IClassicDesktopStyleApplicationLifetime desktop, string message) =>
        _ = MessageDialog.ShowAsync(null, message).ContinueWith(_ => UiThread.Post(() => desktop.Shutdown(1)));
}
