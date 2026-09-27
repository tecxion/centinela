using System.Windows;
using Centinela.Core;
using Centinela.Media;

namespace Centinela.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!SingleInstance.TryClaim())
        {
            // The running instance shows its window; this one has nothing to do.
            Shutdown(0);
            return;
        }
        // Closing the window hides it to the tray; only «Salir» ends the app (MainWindow.OnClosed).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Centinela", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        try
        {
            FFmpegLoader.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Centinela", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        if (!AppPaths.IsDataDirectoryOverridden)
        {
            try { LegacyData.Migrate(LegacyData.Directory, AppPaths.DataDirectory); }
            catch (Exception) { /* start empty; the user can import a copy */ }
        }
        try
        {
            var startHidden = e.Args.Contains("--tray");
            var window = new MainWindow(startHidden);
            if (!startHidden) window.Show();
            SingleInstance.ListenForActivation(() => Dispatcher.BeginInvoke(window.ShowFromTray));
        }
        catch (Exception ex)
        {
            // Without this the process would keep running with no window.
            MessageBox.Show($"No se pudo abrir Centinela: {ex.Message}", "Centinela", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
