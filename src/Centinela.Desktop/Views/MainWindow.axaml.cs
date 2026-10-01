using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Centinela.Core;
using Centinela.Desktop.Services.Backup;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.Services.Tiles;
using Centinela.Desktop.Services.Ui;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    readonly StreamLeases _leases = new();
    readonly TileHost _tiles;
    // Shutdowns of tiles removed from the grid; closing waits for them too (UI thread only).
    readonly List<Task> _pendingShutdowns = [];
    WindowState _stateBeforeFullscreen = WindowState.Normal;
    bool _closing;
    // Last normal (not maximized/fullscreen) placement, saved like WPF's RestoreBounds.
    PixelPoint _normalPos;
    Size _normalSize;
    bool _hasNormal;

    public MainWindow() : this(startHidden: false)
    {
    }

    /// <param name="startHidden">Started with the system (<c>--tray</c>): stays in the tray, if there is one, until opened.</param>
    public MainWindow(bool startHidden)
    {
        InitializeComponent();
        var cameras = new CameraRepository(new CameraStore(CameraStore.DefaultPath, App.Platform.Secrets));
        cameras.Load();
        _vm = new MainViewModel(cameras, new SettingsRepository(new SettingsStore(SettingsStore.DefaultPath)),
            new AutomaticBackup((folder, list) => BackupWriter.WriteAutomatic(folder, list)));
        DataContext = _vm;
        var factory = new TileFactory(_leases, () => _vm.ShowStats);
        factory.Created += Wire;
        _tiles = new TileHost(TileGrid, factory, cameras.Find);
        _tiles.ShutdownStarted += Track;
        InitRecording();
        InitAudio();
        InitCameraActions();
        InitTray();
        try
        {
            InitRest(startHidden);
        }
        catch
        {
            // The window will never show: remove the tray icon so no ghost remains, stop detecting, release the
            // shared sessions (detection's and any tile's) and the logs and sounds, as the WPF window does.
            ReleaseAfterFailedStart();
            throw;
        }
    }

    void InitRest(bool startHidden)
    {
        InitNotices();
        InitErrors();
        // Detection runs also when starting hidden; tiles created from here on show its red frame.
        InitMotion();
        InitUpdates();
        // Started with the system: created but not shown; it counts as hidden in the tray (no tiles).
        IsHiddenInTray = startHidden && HasTray;
        _vm.ViewChanged += Rebuild;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowStats))
                foreach (var tile in AllTiles()) tile.ViewModel.ShowStats = _vm.ShowStats;
        };
        RestorePlacement();
        // On Win32 WM_MOVE/WM_SIZE arrive before WindowState turns Maximized: look after they settle.
        PositionChanged += (_, _) => Dispatcher.UIThread.Post(RememberNormal, DispatcherPriority.Background);
        Resized += (_, _) => Dispatcher.UIThread.Post(RememberNormal, DispatcherPriority.Background);
        Rebuild();
    }

    void ReleaseAfterFailedStart()
    {
        _closing = true;
        // Whatever was created before the failure; fields not reached yet are still null.
        _motion?.Dispose();
        _leases.Dispose();
        _audio?.Dispose();
        _noticeSounds.Dispose();
        _tray?.Dispose();
        _errorLog?.Dispose();
        _motionLog?.Dispose();
    }

    void Rebuild()
    {
        // Hidden in the tray there are no tiles; ShowFromTray rebuilds the view.
        if (!_closing && !IsHiddenInTray) _tiles.Apply(_vm.Layout.Plan());
    }

    void Wire(CameraTileView tile)
    {
        tile.Clicked += t => _vm.Click(t.ViewModel.Camera.Id);
        tile.SwapRequested += _vm.Drop;
        tile.FullscreenRequested += t => ShowFullscreen(t.ViewModel.Camera);
        WireRecording(tile);
        WireAudio(tile);
        WireCameraActions(tile);
        WireErrors(tile);
        WireMotion(tile);
    }

    void ShowFullscreen(Camera shown)
    {
        var window = new FullscreenWindow(_vm.Cameras.CurrentOr(shown), _vm.ShowStats);
        WireRecording(window.Tile);
        WireAudio(window.Tile);
        WireCameraActions(window.Tile);
        WireErrors(window.Tile);
        WireMotion(window.Tile);
        _fullscreens.Add(window);
        window.Closed += (_, _) =>
        {
            _fullscreens.Remove(window);
            _audio.Release(window.Tile.ViewModel);
            Track(window.ShutdownAsync(), shown.Name);
        };
        window.Show(this);
    }

    void Track(Task shutdown, string name)
    {
        _pendingShutdowns.Add(shutdown);
        shutdown.ContinueWith(done => UiThread.Post(() =>
        {
            _pendingShutdowns.Remove(shutdown);
            if (done.Exception?.GetBaseException().Message is { } error && !_closing)
                _vm.Notify($"Error al detener la cámara {name}: {error}", null);
        }), TaskScheduler.Default);
    }

    void Exit_Click(object? sender, RoutedEventArgs e) => ExitApp();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen) ToggleFullscreen();
        else if (e.Key is Key.D0 or Key.NumPad0
                 && _tiles.All.FirstOrDefault(t => t.IsPointerOver && t.ViewModel.IsZoomed) is { } tile)
        {
            // Key 0: back to the whole image of the tile under the pointer.
            tile.ResetZoom();
            e.Handled = true;
        }
    }

    void ToggleFullscreen()
    {
        var entering = WindowState != WindowState.FullScreen;
        if (entering) _stateBeforeFullscreen = WindowState;
        MainMenu.IsVisible = TopBar.IsVisible = BottomBar.IsVisible = !entering;
        WindowState = entering ? WindowState.FullScreen : _stateBeforeFullscreen;
    }

    // settings.json keeps Left/Top in DIPs of the primary monitor, as the WPF app writes them.
    double PrimaryScale => Screens.Primary?.Scaling ?? 1;

    void RestorePlacement()
    {
        var s = _vm.Settings.Settings;
        Width = s.Width;
        Height = s.Height;
        var screens = Screens.All.Select(sc => (sc.Bounds.X, sc.Bounds.Y, sc.Bounds.Width, sc.Bounds.Height));
        if (s.Left is { } left && s.Top is { } top)
        {
            var scale = PrimaryScale;
            int x = WindowPlacement.ToPixels(left, scale), y = WindowPlacement.ToPixels(top, scale);
            if (WindowPlacement.IsVisibleOn(screens, x, y))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(x, y);
            }
        }
        if (s.Maximized) WindowState = WindowState.Maximized;
    }

    void RememberNormal()
    {
        if (WindowState != WindowState.Normal || !IsVisible) return;
        _normalPos = Position;
        _normalSize = new Size(Width, Height);
        _hasNormal = true;
    }

    void SavePlacement()
    {
        if (WindowState == WindowState.FullScreen) ToggleFullscreen();
        if (WindowState == WindowState.Normal) RememberNormal();
        var s = _vm.Settings.Settings;
        s.Maximized = WindowState == WindowState.Maximized;
        if (_hasNormal)
        {
            var scale = PrimaryScale;
            s.Left = WindowPlacement.ToDips(_normalPos.X, scale);
            s.Top = WindowPlacement.ToDips(_normalPos.Y, scale);
            s.Width = _normalSize.Width;
            s.Height = _normalSize.Height;
        }
        _vm.Settings.Save();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Logging off or shutting down Windows, or the app ending: a real exit, never hidden.
        var systemExit = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!_exitRequested && !systemExit && HasTray && !_closing)
        {
            // The X hides to the tray; recordings keep running.
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
        if (e.Cancel) return;
        // The close goes ahead: from here on nothing touches the window or the view model.
        if (!IsHiddenInTray) SavePlacement();
        _closing = true;
        // Fullscreen views now, so their shutdowns are tracked before OnClosed waits.
        foreach (var owned in OwnedWindows.ToList()) owned.Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        foreach (var owned in OwnedWindows.ToList()) owned.Close();
        // Tiles, recordings, the automatic copy and a manual export finish in parallel; one bounded wait so recordings get their trailer.
        var shutdowns = _tiles.ShutdownAll().Concat(_pendingShutdowns)
            .Append(_vm.Recordings.ShutdownAsync()).Append(_vm.Backup.IdleAsync()).Append(_manualExport)
            // Ends active motion events now (on this thread, logging them) and releases detection's leases, stopping
            // the sessions nothing else holds; awaited below with the rest.
            .Append(_motion.ShutdownAsync())
            .ToArray();
        // Stop whatever else still holds a shared session now, so it winds down in parallel.
        _leases.Dispose();
        try
        {
            Task.WaitAll(shutdowns, TimeSpan.FromSeconds(8));
        }
        catch (AggregateException)
        {
            // Failures cannot be shown any more; exit anyway.
        }
        _audio.Dispose();
        _noticeSounds.Dispose();
        _tray?.Dispose();
        _errorLog.Dispose();
        // After _motion.ShutdownAsync above, which logged the events still active.
        _motionLog.Dispose();
        // Raises Closed: the app ends there (App.Start).
        base.OnClosed(e);
    }
}
