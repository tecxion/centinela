using Avalonia.Input;
using Avalonia.Interactivity;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

/// <summary>Recording and snapshot buttons, the recordings folder, and the status line that reveals a path.</summary>
public partial class MainWindow
{
    void InitRecording() => _vm.Recordings.StatusChanged += (id, status) =>
    {
        foreach (var tile in TilesOf(id)) tile.ViewModel.SetRecordingStatus(status);
    };

    /// <summary>A new tile (grid or fullscreen): its ⏺ and 📷, and the camera's recording as it is now.</summary>
    void WireRecording(CameraTileView tile)
    {
        tile.RecordRequested += t => _vm.ToggleRecording(t.ViewModel.Camera);
        tile.SnapshotRequested += t => _ = _vm.SnapshotAsync(t.ViewModel);
        tile.ViewModel.SetRecordingStatus(_vm.Recordings.StatusOf(tile.ViewModel.Camera.Id));
    }

    /// <summary>The toolbar records the cameras on screen; every camera while the window is in the tray.</summary>
    void RecordAll_Click(object? sender, RoutedEventArgs e) => ToggleRecordAll(allCameras: IsHiddenInTray);

    /// <summary>«Grabar todas» / «Detener todas»; the tray menu passes <paramref name="allCameras"/> = true.</summary>
    void ToggleRecordAll(bool allCameras)
    {
        if (!_closing) _vm.ToggleRecordAll(allCameras);
    }

    void OpenRecordings_Click(object? sender, RoutedEventArgs e) =>
        App.Platform.Files.OpenFolder(MainViewModel.EnsureRecordingsFolder());

    void StatusText_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _vm.StatusRevealPath is { } path) App.Platform.Files.Reveal(path);
    }
}
