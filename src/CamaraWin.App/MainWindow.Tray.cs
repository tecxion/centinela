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
        _tray = new TrayController(_autoStart.IsEnabled);
        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
        _tray.RecordAllRequested += ToggleRecordAll;
        _tray.AutoStartToggled += enabled =>
        {
            try
            {
                if (enabled) _autoStart.Enable();
                else _autoStart.Disable();
            }
            catch (Exception ex)
            {
                Notify($"No se pudo cambiar el arranque con Windows: {ex.Message}", null);
                _tray.SetAutoStart(_autoStart.IsEnabled);
            }
        };
        _recordings.StatusChanged += (_, _) =>
        {
            if (!_closed) _tray.SetRecording(_recordings.AnyRecording);
        };
        // Logging off closes the window without «Salir»: that must be a real exit, not a hide.
        Application.Current.SessionEnding += (_, _) => _exitRequested = true;
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

    // Task 11
    void ToggleRecordAll() { }
}
