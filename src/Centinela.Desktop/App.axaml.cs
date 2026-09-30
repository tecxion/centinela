using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Centinela.Core;
using Centinela.Desktop.Services.Ui;
using Centinela.Desktop.Views;
using Centinela.Media;
using Centinela.Platform;

namespace Centinela.Desktop;

public partial class App : Application
{
    internal static PlatformServices Platform { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) Start(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            Platform = PlatformServices.Create(Environment.ProcessPath!);
        }
        catch (PlatformNotSupportedException ex)
        {
            Fail(desktop, ex.Message);
            return;
        }
        if (!Platform.SingleInstance.TryClaim())
        {
            // The running instance shows its window; this one has nothing to do.
            desktop.Shutdown(0);
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

    /// <summary>Shows why Centinela cannot start and ends the process once the message is closed.</summary>
    static void Fail(IClassicDesktopStyleApplicationLifetime desktop, string message) =>
        _ = MessageDialog.ShowAsync(null, message).ContinueWith(_ => UiThread.Post(() => desktop.Shutdown(1)));
}
