using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Services.Cameras;

/// <summary>
/// The per-camera options a tile can flip: HD/SD when big, «Suavizar imagen» and motion detection (👁). Each one
/// changes the stored camera with that Id (a tile may hold an older copy), saves, and shows the new value on every
/// tile of the camera (grid and fullscreen). UI thread only.
/// </summary>
sealed class CameraActions(CameraRepository cameras, Func<Guid, IEnumerable<TileViewModel>> tilesOf, StreamLeases leases)
{
    /// <summary>The grid must be rebuilt: a camera's big view switched stream.</summary>
    public event Action? ViewChanged;

    /// <summary>A new tile shows the camera's options as they are now.</summary>
    public void Show(TileViewModel tile)
    {
        var camera = cameras.CurrentOr(tile.Camera);
        tile.SetLowQuality(camera.LowQualityWhenBig);
        tile.SetSmoothing(camera.Smoothing);
        tile.SetMotionEnabled(camera.MotionEnabled);
    }

    /// <summary>HD/SD: the camera's big view uses the substream (SD) or the main stream (HD).</summary>
    public void ToggleQuality(Guid id)
    {
        if (cameras.Find(id) is not { } camera) return;
        camera.LowQualityWhenBig = !camera.LowQualityWhenBig;
        cameras.Save();
        foreach (var tile in tilesOf(id)) tile.SetLowQuality(camera.LowQualityWhenBig);
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// «Suavizar imagen»: applied live to every session showing the camera (own sessions through their tiles, shared
    /// substream sessions directly).
    /// </summary>
    public void ToggleSmoothing(Guid id)
    {
        if (cameras.Find(id) is not { } camera) return;
        camera.Smoothing = !camera.Smoothing;
        cameras.Save();
        foreach (var tile in tilesOf(id)) tile.SetSmoothing(camera.Smoothing);
        foreach (var session in leases.SessionsFor(id)) session.Smoothing = camera.Smoothing;
    }

    /// <summary>👁: turns the camera's motion detection on or off (the flag and its indicator; detection is phase 2d).</summary>
    public void ToggleMotion(Guid id)
    {
        if (cameras.Find(id) is not { } camera) return;
        camera.MotionEnabled = !camera.MotionEnabled;
        cameras.Save();
        foreach (var tile in tilesOf(id)) tile.SetMotionEnabled(camera.MotionEnabled);
    }
}
