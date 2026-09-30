using Avalonia.Controls;

namespace Centinela.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Another launch asked for the window: show it on top.</summary>
    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
