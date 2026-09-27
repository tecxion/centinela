using System.Windows;
using Centinela.Core;
using Centinela.Media;

namespace Centinela.App;

/// <summary>Error reporting: log, throttled toasts and the error log window.</summary>
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
        _errors = new ErrorCenter(_errorLog);
        _errors.ToastRequested += ShowToast;
        _errors.UnreadChanged += n => LogButton.Content = n == 0 ? "Registro" : $"Registro ({n})";
        Toasts.OpenLogRequested += OpenLog;
        _recordings.ErrorOccurred += (camera, error) => _errors.Report(camera, error);
        _recordings.SessionStateChanged += (camera, state) =>
        {
            if (state == SessionState.Playing) _errors.Playing(camera);
        };
    }

    /// <summary>Live-view tiles (grid, placeholder or fullscreen) report into the error center.</summary>
    void WireErrors(CameraTile tile)
    {
        tile.ErrorReported += (t, error) => _errors.Report(t.Camera, error);
        tile.PlayingReached += t => _errors.Playing(t.Camera);
    }

    /// <summary>In-window toast, or the tray balloon while the window is hidden.</summary>
    void ShowToast(Toast toast)
    {
        if (_closed) return;
        if (IsHiddenInTray) _tray.ShowBalloon(toast.Title, toast.Message);
        else Toasts.Show(toast);
    }

    void OpenLog_Click(object sender, RoutedEventArgs e) => OpenLog();

    void OpenLog()
    {
        _errors.MarkRead();
        if (_logWindow is { IsLoaded: true })
        {
            _logWindow.Activate();
            return;
        }
        _logWindow = new ErrorLogWindow(_errorLog) { Owner = this };
        _logWindow.Show();
    }
}
