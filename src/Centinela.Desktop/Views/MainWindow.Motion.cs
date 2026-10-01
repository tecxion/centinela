using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Motion;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>
/// Motion detection: red frame on every view of the camera, the system notice and its sound, «Movimiento (n)» and
/// the «Registro de movimiento» window. Detection follows every change of the cameras' options.
/// </summary>
public partial class MainWindow
{
    MotionService _motion = null!;
    MotionLog _motionLog = null!;
    MotionLogWindow? _motionLogWindow;
    int _unreadMotion;

    /// <summary>Runs in the constructor, also when starting hidden: detection works with the window in the tray.</summary>
    void InitMotion()
    {
        try { MotionLog.PurgeOlderThan(AppPaths.LogsDirectory, DateTime.Now); }
        catch (Exception) { /* old logs are only a nuisance; never block startup */ }
        _motionLog = new MotionLog(AppPaths.LogsDirectory);
        // Detection decodes the shared substream at 320×180 (more when a thumbnail asks for it).
        _motion = new MotionService(c => _leases.AcquireSub(c, 320, 180));
        _motion.MotionChanged += (id, active) =>
        {
            foreach (var tile in TilesOf(id)) tile.ViewModel.SetMotionActive(active);
        };
        _motion.MotionStarted += OnMotionStarted;
        _motion.MotionEnded += OnMotionEnded;
        _motion.DetectorFailed += (camera, message) =>
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Movimiento",
                $"{camera.Name}: la detección de movimiento se ha desactivado", message));
        _actions.DetectionChanged += ApplyMotion;
        ApplyMotion();
    }

    /// <summary>After any change of the cameras or their detection options: watches exactly the cameras that need it.</summary>
    void ApplyMotion()
    {
        if (!_closing) _motion.Apply(_vm.Cameras.All);
    }

    /// <summary>A new tile (grid, placeholder or fullscreen) shows whether its camera has motion now.</summary>
    void WireMotion(CameraTileView tile) => tile.ViewModel.SetMotionActive(_motion.IsActive(tile.ViewModel.Camera.Id));

    /// <summary>
    /// A system notice, only with the window minimized or hidden (the notification gate decides) and only if there is
    /// a tray to show it. Only a notice actually shown restarts the camera's cooldown.
    /// </summary>
    void OnMotionStarted(Camera camera, bool alertEligible)
    {
        if (!alertEligible || _closing) return;
        var decision = NotificationGate.Decide(NoticeKind.Motion, camera, _vm.Settings.Settings, WindowVisible, DateTime.Now);
        if (decision.Show && _tray is not null)
        {
            _tray.ShowNotice($"Detección de movimiento: «{camera.Name}»", DateTime.Now.ToString("HH:mm:ss"),
                () => UiThread.Post(ShowFromTray));
            _motion.ConfirmAlert(camera.Id, DateTimeOffset.Now);
        }
        if (decision.PlaySound) _noticeSounds.Play(NoticeKind.Motion);
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

    /// <summary>«Movimiento»: marks everything read and shows the motion log (brought forward if open).</summary>
    void OpenMotionLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_closing) return;
        SetUnreadMotion(0);
        if (_motionLogWindow is not null)
        {
            if (_motionLogWindow.WindowState == WindowState.Minimized) _motionLogWindow.WindowState = WindowState.Normal;
            _motionLogWindow.Activate();
            return;
        }
        var window = new MotionLogWindow(_motionLog);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_motionLogWindow, window)) _motionLogWindow = null;
        };
        _motionLogWindow = window;
        window.Show(this);
    }
}
