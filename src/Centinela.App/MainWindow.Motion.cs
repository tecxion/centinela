using System.Media;
using System.Windows;
using Centinela.Core;

namespace Centinela.App;

/// <summary>Motion detection: red frame on every view of the camera, the 👁 toggle and the tray notice.</summary>
public partial class MainWindow
{
    MotionService _motion = null!;

    /// <summary>Runs in the constructor, also when starting hidden: detection works with the window in the tray.</summary>
    void InitMotion()
    {
        // Detection decodes the shared substream at 320×180 (more when a thumbnail asks for it).
        _motion = new MotionService(c => AcquireSub(c, 320, 180), Dispatcher);
        _motion.MotionChanged += (id, active) =>
        {
            foreach (var tile in TilesOf(id)) tile.SetMotionActive(active);
        };
        _motion.MotionStarted += OnMotionStarted;
        _motion.DetectorFailed += (camera, message) =>
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Movimiento",
                $"{camera.Name}: la detección de movimiento se ha desactivado", message));
        _motion.Apply(_cameras);
    }

    /// <summary>Visible to the user: shown, not in the tray and not minimized.</summary>
    bool WindowVisible => !IsHiddenInTray && IsVisible && WindowState != WindowState.Minimized;

    void OnMotionStarted(Camera camera, bool alert)
    {
        if (!alert || _closed) return;
        var decision = NotificationGate.Decide(NoticeKind.Motion, camera, _settings, WindowVisible, DateTime.Now);
        if (decision.Show)
            _tray.ShowBalloon($"Detección de movimiento: «{camera.Name}»", DateTime.Now.ToString("HH:mm:ss"), ShowFromTray);
        if (decision.PlaySound) SystemSounds.Asterisk.Play();
    }

    /// <summary>Shows a new tile's motion state (red frame, 👁) and wires its toggle.</summary>
    void WireMotion(CameraTile tile)
    {
        var camera = _cameras.FirstOrDefault(c => c.Id == tile.Camera.Id) ?? tile.Camera;
        tile.SetMotionEnabled(camera.MotionEnabled);
        tile.SetMotionActive(_motion.IsActive(tile.Camera.Id));
        tile.MotionToggleRequested += ToggleMotion;
    }

    /// <summary>The 👁 button or menu item: flips detection of the current camera with that Id.</summary>
    void ToggleMotion(CameraTile shown)
    {
        if (_cameras.FirstOrDefault(c => c.Id == shown.Camera.Id) is not { } camera) return;
        camera.MotionEnabled = !camera.MotionEnabled;
        SaveCameras();
        _motion.Apply(_cameras);
        foreach (var tile in TilesOf(camera.Id)) tile.SetMotionEnabled(camera.MotionEnabled);
    }
}
