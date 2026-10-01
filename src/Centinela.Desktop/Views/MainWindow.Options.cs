using Avalonia.Interactivity;
using Centinela.Desktop.Services.Notifications;

namespace Centinela.Desktop.Views;

/// <summary>Ver › «Opciones de avisos…»: per-camera detection and alerts, sounds and quiet hours.</summary>
public partial class MainWindow
{
    async void NotificationOptions_Click(object? sender, RoutedEventArgs e)
    {
        var window = new NotificationOptionsWindow(_vm.Cameras.All, _vm.Settings.Settings);
        if (!await window.ShowDialog<bool>(this) || _closing || window.Sounds is not { } sounds) return;
        NotificationOptions.Apply(window.Rows, _vm.Cameras.All, _vm.Settings.Settings, sounds);
        _vm.Cameras.Save();
        _vm.Settings.Save();
        ApplyMotion();
        // The 👁 of every tile shows the camera's detection.
        foreach (var tile in AllTiles()) _actions.Show(tile.ViewModel);
    }
}
