using Avalonia.Interactivity;

namespace Centinela.Desktop.Views;

/// <summary>«🔍 Buscar en red»: finds ONVIF cameras and opens «Añadir cámara» pre-filled with the chosen one.</summary>
public partial class MainWindow
{
    async void Discover_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _vm.Settings.Settings;
        var dialog = new DiscoveryDialog(_vm.Cameras.All.Select(c => c.Host), settings.DiscoveryNetworks);
        var added = await dialog.ShowDialog<bool>(this);
        if (!dialog.SavedNetworks.SequenceEqual(settings.DiscoveryNetworks))
        {
            settings.DiscoveryNetworks = [.. dialog.SavedNetworks];
            _vm.Settings.Save();
        }
        if (added && !_closing && dialog.Result is { } found) await AddCameraAsync(found, this);
    }
}
