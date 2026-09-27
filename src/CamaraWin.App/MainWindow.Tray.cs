using System.Windows;
using CamaraWin.Core;

namespace CamaraWin.App;

/// <summary>Tray mode: the window's X hides to the notification area; only «Salir» ends the app.</summary>
public partial class MainWindow
{
    TrayController _tray = null!;
    AutoStart _autoStart = null!;
    bool _exitRequested;

    public bool IsHiddenInTray { get; private set; }

    void InitTray()
    {
        _autoStart = new AutoStart(new RegistryRunKey(), Environment.ProcessPath!);
        _tray = new TrayController(IsAutoStartEnabled());
        _tray.OpenRequested += ShowFromTray;
        // Deferred so the context menu closes before the exit wait or the sessions start.
        _tray.ExitRequested += () => Dispatcher.BeginInvoke(ExitApp);
        _tray.RecordAllRequested += () => Dispatcher.BeginInvoke(ToggleRecordAll);
        _tray.MenuOpening += RefreshAutoStartCheck;
        _tray.AutoStartToggled += enabled =>
        {
            if (enabled == IsAutoStartEnabled()) return;
            try
            {
                if (enabled) _autoStart.Enable();
                else _autoStart.Disable();
            }
            catch (Exception ex)
            {
                Notify($"No se pudo cambiar el arranque con Windows: {ex.Message}", null);
                RefreshAutoStartCheck();
            }
        };
        _recordings.Failure += NotifyFailure;
        _recordings.StatusChanged += (_, _) =>
        {
            if (_closed) return;
            _tray.SetRecording(_recordings.AnyRecording);
            RecordAllButton.Content = _recordings.AnyRecording ? "⏹ Detener todas" : "⏺ Grabar todas";
        };
        // Logging off closes the window without «Salir»: that must be a real exit, not a hide.
        Application.Current.SessionEnding += (_, _) => _exitRequested = true;
    }

    bool IsAutoStartEnabled()
    {
        try { return _autoStart.IsEnabled; }
        catch (Exception) { return false; }
    }

    /// <summary>The registry value can change outside the app (Task Manager, another copy of the exe).</summary>
    void RefreshAutoStartCheck() => _tray.SetAutoStart(IsAutoStartEnabled());

    /// <summary>Failure notices go to the status bar, and to a tray balloon while the window is hidden.</summary>
    void NotifyFailure(string message)
    {
        if (_closed) return;
        Notify(message, null);
        if (IsHiddenInTray) _tray.ShowBalloon("CamaraWin", message);
    }

    /// <summary>Stops every live view (recordings keep running) and hides the window.</summary>
    public void HideToTray()
    {
        if (IsHiddenInTray || _closed) return;
        SaveWindowPlacement();
        // Fullscreen views track their own shutdowns when closed.
        foreach (var owned in OwnedWindows.Cast<Window>().ToList()) owned.Close();
        foreach (var key in _tiles.Keys.ToList()) DisposeTile(key);
        foreach (var placeholder in _placeholders.Values.ToList()) RemoveAndShutdown(placeholder);
        _placeholders.Clear();
        Hide();
        IsHiddenInTray = true;
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            SaveSettingsQuietly();
            _tray.ShowBalloon("CamaraWin sigue en la bandeja", "Las grabaciones continúan. Doble clic en el icono para abrirla; «Salir» la cierra.");
        }
    }

    public void ShowFromTray()
    {
        if (_closed || _exitRequested) return;
        if (IsHiddenInTray)
        {
            IsHiddenInTray = false;
            Show();
            RebuildView();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>The real exit (menu or tray «Salir»): OnClosing/OnClosed stop everything and end the app.</summary>
    void ExitApp()
    {
        if (_closed) return;
        _exitRequested = true;
        Close();
    }

    void RecordAll_Click(object sender, RoutedEventArgs e) => ToggleRecordAll();

    /// <summary>
    /// Stops every recording if any is running; otherwise records the cameras on screen
    /// (every camera while the window is in the tray).
    /// </summary>
    void ToggleRecordAll()
    {
        if (_closed) return;
        if (_recordings.AnyRecording)
        {
            _recordings.StopAllWithNotice();
            return;
        }
        var ids = (IsHiddenInTray ? _cameras.Select(c => c.Id) : PlanView().Slots.Select(s => s.CameraId)).ToHashSet();
        foreach (var camera in _cameras.Where(c => ids.Contains(c.Id)).ToList()) _recordings.Start(camera);
    }
}
