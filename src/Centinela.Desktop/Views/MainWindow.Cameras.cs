using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Cameras;

namespace Centinela.Desktop.Views;

/// <summary>
/// Per-camera actions from the tiles (HD/SD, «Suavizar imagen», 👁, «Editar…», «Duplicar…», «Eliminar»), «➕ Añadir cámara»
/// and the open fullscreen views.
/// </summary>
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
        tile.EditRequested += t => _ = EditCameraAsync(t);
        tile.DuplicateRequested += t => _ = DuplicateCameraAsync(t);
        tile.DeleteRequested += t => _ = DeleteCameraAsync(t);
        tile.EncoderInfoRequested += ShowQuality;
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
        _errors.Forget(camera.Id);
        DisposeTilesOf(camera.Id);
        _vm.RemoveCamera(camera.Id);
        ApplyMotion();
    }

    void AddCamera_Click(object? sender, RoutedEventArgs e) => _ = AddCameraAsync(null, this);

    /// <summary>«Añadir cámara», empty or pre-filled (a duplicate); the camera goes last in the order when saved.</summary>
    async Task AddCameraAsync(Camera? prefilled, Window owner)
    {
        var dialog = new AddCameraDialog(prefilled, isNew: true);
        if (!await dialog.ShowDialog<bool>(owner) || _closing) return;
        _errors.Forget(dialog.Result.Id);
        _vm.AddCamera(dialog.Result);
        ApplyMotion();
    }

    /// <summary>Opens «Añadir cámara» pre-filled with a copy (new Id, "(copia)"); nothing is saved unless the user saves.</summary>
    Task DuplicateCameraAsync(CameraTileView tile) =>
        _vm.Cameras.Find(tile.ViewModel.Camera.Id) is { } camera
            ? AddCameraAsync(camera.Duplicate(), OwnerFor(tile))
            : Task.CompletedTask;

    async Task EditCameraAsync(CameraTileView tile)
    {
        if (_vm.Cameras.Find(tile.ViewModel.Camera.Id) is not { } camera) return;
        var dialog = new AddCameraDialog(camera, isNew: false);
        if (!await dialog.ShowDialog<bool>(OwnerFor(tile))) return;
        // Deleted meanwhile, or the window is closing.
        if (_closing || _vm.Cameras.Find(camera.Id) is null) return;
        // New settings (often a corrected password) deserve fresh notices.
        _errors.Forget(camera.Id);
        // The URL may change: its tiles and fullscreen views restart with the new settings.
        DisposeTilesOf(camera.Id);
        _vm.ReplaceCamera(dialog.Result);
        ApplyMotion();
    }

    /// <summary>«Calidad de la cámara…», without blocking: the camera as it is now, over the window showing the tile.</summary>
    void ShowQuality(CameraTileView tile)
    {
        var camera = _vm.Cameras.CurrentOr(tile.ViewModel.Camera);
        var id = camera.Id;
        new CameraQualityWindow(camera, text => SaveOriginals(id, text)).Show(OwnerFor(tile));
    }

    /// <summary>
    /// Keeps the encoder values found before Centinela's first change on the stored camera (it may have been edited
    /// or replaced since the window opened); never overwrites originals already kept.
    /// </summary>
    void SaveOriginals(Guid cameraId, string originals)
    {
        if (_vm.Cameras.Find(cameraId) is not { } camera || EncoderSnapshot.Parse(camera.OriginalEncoders).Count > 0) return;
        camera.OriginalEncoders = originals;
        _vm.Cameras.Save();
    }

    /// <summary>The dialog opens over the window showing the tile (a fullscreen view or this one).</summary>
    Window OwnerFor(CameraTileView tile) => TopLevel.GetTopLevel(tile) as Window ?? this;
}
