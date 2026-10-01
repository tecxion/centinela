using Avalonia.Interactivity;
using Centinela.Desktop.Services.Info;

namespace Centinela.Desktop.Views;

/// <summary>The Ayuda menu: manual, licence and support windows, and «Acerca de».</summary>
public partial class MainWindow
{
    void Manual_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Manual de Centinela", ManualDocument.Build(Os)).Show(this);

    void License_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Licencia", InfoDocuments.License()).Show(this);

    void Support_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Soporte", InfoDocuments.Support()).Show(this);

    void About_Click(object? sender, RoutedEventArgs e) => _ = new AboutWindow().ShowDialog(this);
}
