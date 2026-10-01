using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core.Platform;
using Centinela.Desktop.Services.Tray;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>
/// Tray mode: with a tray, the window's X hides it to the tray and recordings keep running; only «Salir» (menu or
/// tray) or the end of the user's session ends the app. Without a tray, the X ends the app on Windows and minimizes
/// the window on Linux and macOS, where the auto start check moves to the Archivo menu.
/// </summary>
public partial class MainWindow
{
    static readonly OsKind Os = OsInfo.Current;
    ITray? _tray;
    IAutoStart? _autoStart;
    bool _exitRequested;
    // The icon shown now (null before the first) and both icons, drawn once.
    bool? _trayRecording;
    readonly Dictionary<bool, TrayImage> _trayIcons = [];

    /// <summary>Hidden in the tray: no tiles, no audio; recordings keep running.</summary>
    public bool IsHiddenInTray { get; private set; }

    /// <summary>The tray is there: the X hides the window instead of ending the app.</summary>
    bool HasTray => _tray is not null;

    void InitTray()
    {
        _autoStart = App.Platform.AutoStart;
        if (_autoStart is not null)
        {
            try { _autoStart.Migrate(); }
            catch (Exception) { /* the old entry just stays; the tray check shows the real state */ }
        }
        try
        {
            _tray = NewTray();
            if (_tray is null)
            {
                ShowAutoStartInMenu();
                return;
            }
            // Both icons now, so a drawing failure means no tray rather than an error later.
            foreach (var recording in new[] { false, true }) _trayIcons[recording] = TrayIconRenderer.Render(recording);
            UpdateTray();
        }
        catch (Exception)
        {
            // No tray: Centinela works as a normal window and the X ends it.
            _tray?.Dispose();
            _tray = null;
            ShowAutoStartInMenu();
            return;
        }
        // Deferred so the menu has closed before the window shows or the exit wait starts.
        _tray.OpenRequested += () => UiThread.Post(ShowFromTray);
        _tray.MenuOpening += UpdateTrayMenu; // the auto start may have changed outside the app
        _vm.Recordings.StatusChanged += (_, _) => UpdateTray();
        _vm.Recordings.Failure += message =>
        {
            // The status line already shows it.
            if (IsHiddenInTray && !_closing) ShowSystemNotice("Centinela", message);
        };
    }

    /// <summary>
    /// Windows' own tray; elsewhere Avalonia's, when the desktop can show it (asked once at startup); else none.
    /// </summary>
    static ITray? NewTray()
    {
        if (App.Platform.CreateTray is { } create) return create();
        return Os != OsKind.Windows && App.TrayAvailable.GetAwaiter().GetResult() ? new AvaloniaTray(App.Platform.Notifier) : null;
    }

    /// <summary>
    /// Without a tray on Linux and macOS, the auto start check lives in the Archivo menu (Windows keeps its menu as
    /// it was).
    /// </summary>
    void ShowAutoStartInMenu()
    {
        if (Os == OsKind.Windows || _autoStart is null) return;
        AutoStartMenuItem.Header = TrayTexts.AutoStartLabel(Os);
        AutoStartMenuItem.IsVisible = true;
        UpdateAutoStartMenuItem();
    }

    /// <summary>Archivo opens: the check shows the real state (it may have changed outside the app).</summary>
    void FileMenu_SubmenuOpened(object? sender, RoutedEventArgs e) => UpdateAutoStartMenuItem();

    void UpdateAutoStartMenuItem()
    {
        if (AutoStartMenuItem.IsVisible) AutoStartMenuItem.IsChecked = IsAutoStartEnabled();
    }

    /// <summary>The menu has flipped its check already; the change asked for is the opposite of the real state.</summary>
    void AutoStart_Click(object? sender, RoutedEventArgs e) => SetAutoStart(!IsAutoStartEnabled());

    /// <summary>The icon (red dot while recording) and the menu («Grabar todas» / «Detener todas»).</summary>
    void UpdateTray()
    {
        if (_tray is null || _closing) return;
        var recording = _vm.Recordings.AnyRecording;
        if (_trayRecording != recording)
        {
            _tray.SetIcon(_trayIcons[recording], "Centinela");
            _trayRecording = recording;
        }
        UpdateTrayMenu();
    }

    void UpdateTrayMenu()
    {
        if (_tray is null || _closing) return;
        _tray.SetMenu(TrayMenu.Build(_vm.Recordings.AnyRecording, _autoStart is not null, IsAutoStartEnabled(), Os,
            open: () => UiThread.Post(ShowFromTray),
            // The tray acts on every camera, even with the window open.
            recordAll: () => UiThread.Post(() => ToggleRecordAll(allCameras: true)),
            setAutoStart: SetAutoStart,
            exit: () => UiThread.Post(ExitApp)));
    }

    bool IsAutoStartEnabled()
    {
        if (_autoStart is null) return false;
        try { return _autoStart.IsEnabled; }
        catch (Exception) { return false; }
    }

    void SetAutoStart(bool enabled)
    {
        if (_closing) return;
        if (_autoStart is null)
        {
            _vm.Notify(TrayTexts.AutoStartUnavailable(Os), null);
            return;
        }
        if (enabled == IsAutoStartEnabled()) return;
        try
        {
            if (enabled) _autoStart.Enable();
            else _autoStart.Disable();
        }
        catch (Exception ex)
        {
            _vm.Notify(TrayTexts.AutoStartFailed(Os, ex.Message), null);
        }
        // The check shows the real state, whether the change worked or not.
        UpdateTrayMenu();
        UpdateAutoStartMenuItem();
    }

    /// <summary>Closes the views (recordings keep running), releases the sound and hides the window.</summary>
    void HideToTray()
    {
        if (IsHiddenInTray || _closing || _tray is null) return;
        SavePlacement();
        // Fullscreen views track their own shutdowns when closed.
        foreach (var owned in OwnedWindows.ToList()) owned.Close();
        _tiles.RetireAll();
        _audio.ReleaseAll();
        Hide();
        IsHiddenInTray = true;
        var settings = _vm.Settings.Settings;
        if (!settings.TrayHintShown)
        {
            settings.TrayHintShown = true;
            _vm.Settings.Save();
            ShowSystemNotice("Centinela sigue en la bandeja",
                "Las grabaciones continúan. Doble clic en el icono para abrirla; «Salir» la cierra.");
        }
    }

    /// <summary>The tray's «Abrir», a double click on it, a click on a notice, or another launch of Centinela.</summary>
    public void ShowFromTray()
    {
        if (_closing || _exitRequested) return;
        if (IsHiddenInTray)
        {
            IsHiddenInTray = false;
            Show();
            Rebuild();
            ShowPendingUpdate();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>The real exit («Salir» in the menu or the tray): OnClosing/OnClosed stop everything; then the app ends.</summary>
    void ExitApp()
    {
        if (_closing) return;
        _exitRequested = true;
        Close();
    }

    /// <summary>The user's session ends (or the system asks the app to quit): a real exit, never a hide.</summary>
    public void ExitForShutdown() => ExitApp();
}
