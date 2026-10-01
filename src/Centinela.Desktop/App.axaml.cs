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

    /// <summary>Why the system pieces could not be created in Program.Main; shown once the UI runs.</summary>
    internal static string? StartupError { get; set; }

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
            var window = new MainWindow();
            desktop.MainWindow = window;
            Platform.SingleInstance.ListenForActivation(() => UiThread.Post(window.BringToFront));
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
            var owner = desktop.MainWindow is { IsVisible: true } window ? window : null;
            _ = MessageDialog.ShowAsync(owner, e.Exception.Message);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
    }

    /// <summary>Shows why Centinela cannot start and ends the process once the message is closed.</summary>
    static void Fail(IClassicDesktopStyleApplicationLifetime desktop, string message) =>
        _ = MessageDialog.ShowAsync(null, message).ContinueWith(_ => UiThread.Post(() => desktop.Shutdown(1)));
}
