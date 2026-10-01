using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Errors;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.Services.Ui;
using Centinela.Media;

namespace Centinela.Desktop.Views;

/// <summary>
/// Error reporting: every connection error goes to the log; throttled notices (with their sound) and the
/// «Registro de errores» window. Own sessions report through their tiles, shared sessions once each, recordings too.
/// </summary>
public partial class MainWindow
{
    ErrorLog _errorLog = null!;
    ErrorCenter _errors = null!;
    ErrorLogWindow? _logWindow;

    void InitErrors()
    {
        try { ErrorLog.PurgeOlderThan(AppPaths.LogsDirectory, DateTime.Now); }
        catch (Exception) { /* old logs are only a nuisance; never block startup */ }
        _errorLog = new ErrorLog(AppPaths.LogsDirectory);
        _errors = new ErrorCenter(_errorLog, (kind, camera) =>
            NotificationGate.Decide(kind, _vm.Cameras.CurrentOr(camera), _vm.Settings.Settings, WindowVisible, DateTime.Now));
        _errors.ToastRequested += toast =>
        {
            if (!_closing) _notices.Show(toast);
        };
        _errors.SoundRequested += kind =>
        {
            if (kind == NoticeKind.ConnectionLost && !_closing) _noticeSounds.Play(kind);
        };
        _errors.UnreadChanged += n => LogButton.Content = n == 0 ? "Registro" : $"Registro ({n})";
        Toasts.OpenLogRequested += OpenLog;
        _vm.Recordings.ErrorOccurred += (camera, error) => _errors.Report(camera, error);
        _vm.Recordings.SessionStateChanged += (camera, state) =>
        {
            if (state == SessionState.Playing) _errors.Playing(camera);
        };
        _leases.SessionCreated += WireSharedErrors;
    }

    /// <summary>Shown, not hidden in the tray and not minimized: the notification gate's "window visible".</summary>
    bool WindowVisible => IsVisible && !IsHiddenInTray && WindowState != WindowState.Minimized;

    /// <summary>
    /// A new tile (grid, placeholder or fullscreen) with its own session reports into the error center. Tiles on a
    /// shared substream never raise these: <see cref="WireSharedErrors"/> reports each shared session once.
    /// </summary>
    void WireErrors(CameraTileView tile)
    {
        tile.ViewModel.ErrorReported += (t, error) => _errors.Report(t.Camera, error);
        tile.ViewModel.Playing += t => _errors.Playing(t.Camera);
    }

    /// <summary>
    /// Once per shared session, before it starts, inside the shared streams' creation callback: only subscribes;
    /// every handler runs later on the UI thread.
    /// </summary>
    void WireSharedErrors(StreamSession session, Camera camera)
    {
        if (StreamLeases.SubUrlOf(camera) is not { } url) return;
        var cameraId = camera.Id;
        var useUdp = camera.UseUdp;
        session.ErrorOccurred += error => UiThread.Post(() =>
        {
            if (CurrentCameraFor(cameraId, url, useUdp) is { } current) _errors.Report(current, error);
        });
        session.StateChanged += state =>
        {
            if (state != SessionState.Playing) return;
            UiThread.Post(() =>
            {
                if (CurrentCameraFor(cameraId, url, useUdp) is { } current) _errors.Playing(current);
            });
        };
    }

    /// <summary>
    /// The current camera with that Id (tiles may hold older copies), or null once it was deleted, the window
    /// closed, or it was edited to another substream: a stopping old session must not report under the new settings.
    /// </summary>
    Camera? CurrentCameraFor(Guid cameraId, string url, bool useUdp) =>
        !_closing && _vm.Cameras.Find(cameraId) is { } camera && StreamLeases.UsesSession(camera, url, useUdp)
            ? camera
            : null;

    void OpenLog_Click(object? sender, RoutedEventArgs e) => OpenLog();

    /// <summary>«Registro», or a click on a notice: marks everything read and shows the log (brought forward if open).</summary>
    void OpenLog()
    {
        if (_closing) return;
        _errors.MarkRead();
        if (_logWindow is not null)
        {
            if (_logWindow.WindowState == WindowState.Minimized) _logWindow.WindowState = WindowState.Normal;
            _logWindow.Activate();
            return;
        }
        var window = new ErrorLogWindow(_errorLog);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_logWindow, window)) _logWindow = null;
        };
        _logWindow = window;
        window.Show(this);
    }
}
