using Avalonia.Interactivity;
using Centinela.Desktop.Services.Info;

namespace Centinela.Desktop.Views;

/// <summary>The Ayuda menu: manual, licence and support windows, and «Acerca de».</summary>
public partial class MainWindow
{
    void Manual_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Manual de Centinela", ManualDocument.Build(Os)).Show(this);

    void License_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Licencia", InfoDocuments.License()).Show(this);

    void Support_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Soporte", InfoDocuments.Support()).Show(this);

    void About_Click(object? sender, RoutedEventArgs e) => ShowAbout();

    /// <summary>Also from the macOS application menu, while the window may be hidden in the menu bar.</summary>
    internal void ShowAbout()
    {
        if (IsHiddenInTray) ShowFromTray();
        // While exiting the window stays hidden, and a dialog needs a visible owner.
        if (!IsVisible) return;
        _ = new AboutWindow().ShowDialog(this);
    }
}
