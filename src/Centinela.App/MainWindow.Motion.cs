using System.Media;
using System.Windows;
using Centinela.Core;

namespace Centinela.App;

/// <summary>Motion detection: red frame on every view of the camera, the 👁 toggle, the tray notice and the motion log.</summary>
public partial class MainWindow
{
    MotionService _motion = null!;
    MotionLog _motionLog = null!;
    MotionLogWindow? _motionLogWindow;
    int _unreadMotion;

    /// <summary>Runs in the constructor, also when starting hidden: detection works with the window in the tray.</summary>
    void InitMotion()
    {
        // Detection decodes the shared substream at 320×180 (more when a thumbnail asks for it).
        try { MotionLog.PurgeOlderThan(AppPaths.LogsDirectory, DateTime.Now); }
        catch (Exception) { /* old logs are only a nuisance; never block startup */ }
        _motionLog = new MotionLog(AppPaths.LogsDirectory);
        _motion = new MotionService(c => AcquireSub(c, 320, 180), Dispatcher);
        _motion.MotionChanged += (id, active) =>
        {
            foreach (var tile in TilesOf(id)) tile.SetMotionActive(active);
        };
        _motion.MotionStarted += OnMotionStarted;
        _motion.MotionEnded += OnMotionEnded;
        _motion.DetectorFailed += (camera, message) =>
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Movimiento",
                $"{camera.Name}: la detección de movimiento se ha desactivado", message));
        _motion.Apply(_cameras);
    }

    /// <summary>Visible to the user: shown, not in the tray and not minimized.</summary>
    bool WindowVisible => !IsHiddenInTray && IsVisible && WindowState != WindowState.Minimized;

    /// <summary>
    /// Only a notice actually shown restarts the camera's cooldown: one hidden by quiet hours, disabled notices or the
    /// visible window leaves the next event free to notify.
    /// </summary>
    void OnMotionStarted(Camera camera, bool alertEligible)
    {
        if (!alertEligible || _closed) return;
        var decision = NotificationGate.Decide(NoticeKind.Motion, camera, _settings, WindowVisible, DateTime.Now);
        if (decision.Show)
        {
            _tray.ShowBalloon($"Detección de movimiento: «{camera.Name}»", DateTime.Now.ToString("HH:mm:ss"), ShowFromTray);
            _motion.ConfirmAlert(camera.Id, DateTimeOffset.Now);
        }
        if (decision.PlaySound) SystemSounds.Asterisk.Play();
    }

    /// <summary>
    /// Logs a finished event, also while closing: <see cref="MotionService.ShutdownAsync"/> ends the active ones
    /// before <see cref="OnClosed"/> disposes the log.
    /// </summary>
    void OnMotionEnded(Camera camera, MotionEvent motionEvent)
    {
        _motionLog.Add(new MotionLogEntry(motionEvent.Start.LocalDateTime, camera.Name,
            motionEvent.End - motionEvent.Start, motionEvent.Peak));
        SetUnreadMotion(_unreadMotion + 1);
    }

    void SetUnreadMotion(int count)
    {
        _unreadMotion = count;
        MotionLogButton.Content = count == 0 ? "Movimiento" : $"Movimiento ({count})";
    }

    void OpenMotionLog_Click(object sender, RoutedEventArgs e)
    {
        SetUnreadMotion(0);
        if (_motionLogWindow is { IsLoaded: true })
        {
            _motionLogWindow.Activate();
            return;
        }
        _motionLogWindow = new MotionLogWindow(_motionLog) { Owner = this };
        _motionLogWindow.Show();
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

    /// <summary>
    /// Ver › Opciones de avisos…: edits copies in a modal window; «Aceptar» writes them into the current cameras (by Id,
    /// ignoring cameras deleted meanwhile) and the settings, saves and applies at once. «Cancelar» changes nothing.
    /// </summary>
    void NotificationOptions_Click(object sender, RoutedEventArgs e)
    {
        var window = new NotificationOptionsWindow([.. _cameras.Select(c => c.Clone())], _settings) { Owner = this };
        if (window.ShowDialog() != true || _closed) return;
        foreach (var row in window.Rows)
        {
            if (_cameras.FirstOrDefault(c => c.Id == row.Id) is not { } camera) continue;
            camera.MotionEnabled = row.MotionEnabled;
            camera.MotionSensitivity = row.Sensitivity;
            camera.MotionCooldownSeconds = Camera.NormalizeCooldown(row.CooldownSeconds);
            camera.ConnectionAlerts = row.ConnectionAlerts;
            camera.MotionAlerts = row.MotionAlerts;
        }
        _settings.SoundOnConnectionLost = window.SoundOnConnectionLost;
        _settings.SoundOnMotion = window.SoundOnMotion;
        _settings.QuietHoursEnabled = window.QuietHoursEnabled;
        _settings.QuietFrom = window.QuietFrom;
        _settings.QuietTo = window.QuietTo;
        SaveCameras();
        SaveSettingsQuietly();
        _motion.Apply(_cameras);
        foreach (var camera in _cameras)
            foreach (var tile in TilesOf(camera.Id)) tile.SetMotionEnabled(camera.MotionEnabled);
    }
}
