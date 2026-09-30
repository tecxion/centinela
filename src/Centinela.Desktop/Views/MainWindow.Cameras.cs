using Centinela.Desktop.Services.Cameras;

namespace Centinela.Desktop.Views;

/// <summary>Per-camera actions from the tiles (HD/SD, «Suavizar imagen», 👁, «Eliminar») and the open fullscreen views.</summary>
public partial class MainWindow
{
    CameraActions _actions = null!;
    // Open fullscreen views, in opening order; each leaves the list when it closes.
    readonly List<FullscreenWindow> _fullscreens = [];

    void InitCameraActions()
    {
        _actions = new CameraActions(_vm.Cameras, id => TilesOf(id).Select(t => t.ViewModel), _leases);
        _actions.ViewChanged += Rebuild;
    }

    /// <summary>A new tile (grid or fullscreen): its option buttons and menu items, and the camera's options as they are.</summary>
    void WireCameraActions(CameraTileView tile)
    {
        tile.QualityToggleRequested += t => _actions.ToggleQuality(t.ViewModel.Camera.Id);
        tile.SmoothingToggleRequested += t => _actions.ToggleSmoothing(t.ViewModel.Camera.Id);
        tile.MotionToggleRequested += t => _actions.ToggleMotion(t.ViewModel.Camera.Id);
        tile.DeleteRequested += t => _ = DeleteCameraAsync(t);
        _actions.Show(tile.ViewModel);
    }

    /// <summary>Every tile on screen: grid tiles (and placeholders) plus fullscreen views.</summary>
    IEnumerable<CameraTileView> AllTiles() => _tiles.All.Concat(_fullscreens.Select(w => w.Tile));

    /// <summary>Every tile showing this camera: its grid tiles (and placeholders) plus any open fullscreen view.</summary>
    IEnumerable<CameraTileView> TilesOf(Guid cameraId) => AllTiles().Where(t => t.ViewModel.Camera.Id == cameraId);

    /// <summary>Removes every tile of the camera (edited or deleted camera) and closes its fullscreen views.</summary>
    void DisposeTilesOf(Guid cameraId)
    {
        _tiles.DisposeTilesOf(cameraId);
        foreach (var window in _fullscreens.Where(w => w.Tile.ViewModel.Camera.Id == cameraId).ToList()) window.Close();
    }

    async Task DeleteCameraAsync(CameraTileView tile)
    {
        var camera = _vm.Cameras.CurrentOr(tile.ViewModel.Camera);
        if (!await MessageDialog.ConfirmAsync(this, $"¿Eliminar la cámara «{camera.Name}»?")) return;
        // Deleted meanwhile, or the window is closing.
        if (_closing || _vm.Cameras.Find(camera.Id) is null) return;
        DisposeTilesOf(camera.Id);
        _vm.RemoveCamera(camera.Id);
    }
}
