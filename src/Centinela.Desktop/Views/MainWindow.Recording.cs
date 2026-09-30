using Avalonia.Input;
using Avalonia.Interactivity;
using Centinela.Core;
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

    void RecordAll_Click(object? sender, RoutedEventArgs e) => _vm.ToggleRecordAll(allCameras: false);

    void OpenRecordings_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        App.Platform.Files.OpenFolder(AppPaths.RecordingsDirectory);
    }

    void StatusText_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _vm.StatusRevealPath is { } path) App.Platform.Files.Reveal(path);
    }
}
