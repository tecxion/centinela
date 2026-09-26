using System.Windows;
using CamaraWin.Media;

namespace CamaraWin.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "CamaraWin", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        try
        {
            FFmpegLoader.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CamaraWin", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        new MainWindow().Show();
    }
}
