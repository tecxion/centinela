using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Centinela.Core;

namespace Centinela.App;

public partial class MainWindow : Window
{
    readonly CameraStore _store = new(CameraStore.DefaultPath);
    readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath);
    readonly AppSettings _settings;
    readonly List<Camera> _cameras;
    readonly RecordingController _recordings;
    // Shutdowns of tiles removed from the grid; OnClosed waits for them too (UI thread only).
    readonly List<Task> _pendingShutdowns = [];
    string? _statusRevealPath;
    static readonly GridMode[] GridModes = [GridMode.Auto, GridMode.One, GridMode.Four, GridMode.Nine, GridMode.Sixteen];
    WindowState _stateBeforeFullscreen;
    // Set once the window has closed: background work finishing later must not rebuild the view.
    bool _closed;

    /// <param name="startHidden">Start in the tray (<c>--tray</c>): no window and no live views until opened.</param>
    public MainWindow(bool startHidden = false)
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        _cameras = [.. _store.Load()];
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;

        RestoreWindowPlacement();
        GridModeBox.SelectedIndex = ComboIndexForSettings();
        GridModeBox.SelectionChanged += GridMode_Changed;
        _recordings = new RecordingController(Dispatcher);
        _recordings.Notify += Notify;
        _recordings.StatusChanged += (id, status) =>
        {
            foreach (var tile in TilesOf(id)) tile.SetRecordingStatus(status);
        };
        ShowStatsItem.IsChecked = _settings.ShowStats;
        InitErrors();
        InitTray();
        InitUpdates();
        try
        {
            if (startHidden) IsHiddenInTray = true;
            else RebuildView();
        }
        catch
        {
            // The window will never show: remove the tray icon so no ghost remains.
            _tray.Dispose();
            throw;
        }
    }

    /// <summary>Every tile showing this camera: its grid tiles (and placeholders) plus any open fullscreen view.</summary>
    IEnumerable<CameraTile> TilesOf(Guid id)
    {
        foreach (var tile in _tiles.Values.Concat(_placeholders.Values))
            if (tile.Camera.Id == id) yield return tile;
        foreach (var window in OwnedWindows.OfType<FullscreenWindow>())
            if (window.Tile.Camera.Id == id) yield return window.Tile;
    }

    /// <summary>
    /// Uses the current camera with that Id (the tile may hold an older copy); does nothing if it was deleted.
    /// </summary>
    void ToggleRecording(Camera shown)
    {
        if (_cameras.FirstOrDefault(c => c.Id == shown.Id) is not { } camera) return;
        if (_recordings.IsRecording(camera.Id)) _recordings.StopWithNotice(camera.Id);
        else _recordings.Start(camera);
    }

    void ShowFullscreen(CameraTile tile)
    {
        var window = new FullscreenWindow(tile.Camera) { Owner = this };
        var name = tile.Camera.Name;
        window.Tile.RecordRequested += t => ToggleRecording(t.Camera);
        window.Tile.Notify += Notify;
        window.Tile.SetRecordingStatus(_recordings.StatusOf(tile.Camera.Id));
        window.Tile.ShowStats = _settings.ShowStats;
        WireErrors(window.Tile);
        window.Tile.AudioCapable = true;
        WireAudio(window.Tile);
        // OnClosed waits for the live session's shutdown like any other.
        window.Closed += (_, _) =>
        {
            ReleaseAudio(window.Tile);
            TrackShutdown(window.Tile.ShutdownAsync(), name);
        };
        window.Show();
    }

    void SwapCameras(Guid source, Guid target)
    {
        var a = _cameras.FirstOrDefault(c => c.Id == source);
        var b = _cameras.FirstOrDefault(c => c.Id == target);
        if (a is null || b is null) return;
        if (IsFeaturedLayout && target == FeaturedCameraId())
        {
            // Dropping a thumbnail onto the featured tile features the dragged camera.
            _settings.FeaturedCameraId = source;
            SaveSettingsQuietly();
            RebuildView();
            return;
        }
        if (_settings.LayoutMode == LayoutMode.Dual && BigCameraIds() is var bigs && bigs.Contains(target))
        {
            // Dropping onto a big camera puts the dragged camera there (swapping when both are big).
            StoreDual(DualSelection.Drop(new DualState(bigs, _settings.DualNextReplace), source, bigs.ToList().IndexOf(target)));
            SaveSettingsQuietly();
            RebuildView();
            return;
        }
        (a.Order, b.Order) = (b.Order, a.Order);
        SaveCameras();
        RebuildView();
    }

    void ShowStats_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowStats = ShowStatsItem.IsChecked;
        SaveSettingsQuietly();
        foreach (var tile in _tiles.Values.Concat(_placeholders.Values)) tile.ShowStats = _settings.ShowStats;
        foreach (var window in OwnedWindows.OfType<FullscreenWindow>()) window.Tile.ShowStats = _settings.ShowStats;
    }

    void GridMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        var index = GridModeBox.SelectedIndex;
        if (index >= GridModes.Length) _settings.LayoutMode = LayoutModes[index - GridModes.Length];
        else
        {
            _settings.LayoutMode = LayoutMode.Grid;
            _settings.GridMode = GridModes[index];
        }
        SaveSettingsQuietly();
        RebuildView();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None) ToggleFullscreen();
        else if (e.Key is Key.D0 or Key.NumPad0 && TileUnderMouse() is { IsZoomed: true } tile)
        {
            tile.ResetZoom();
            e.Handled = true;
        }
    }

    /// <summary>The camera tile under the mouse pointer, if any.</summary>
    static CameraTile? TileUnderMouse()
    {
        // DirectlyOver can be a non-visual content element (e.g. a Run): walk those with the logical tree.
        for (var node = Mouse.DirectlyOver as DependencyObject; node is not null;
             node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is CameraTile tile) return tile;
        return null;
    }

    void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _stateBeforeFullscreen;
            MainMenu.Visibility = TopBar.Visibility = BottomBar.Visibility = Visibility.Visible;
        }
        else
        {
            _stateBeforeFullscreen = WindowState;
            MainMenu.Visibility = TopBar.Visibility = BottomBar.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal; // re-maximizing after the style change covers the taskbar
            WindowState = WindowState.Maximized;
        }
    }

    void RestoreWindowPlacement()
    {
        Width = _settings.Width;
        Height = _settings.Height;
        if (_settings.Left is { } left && _settings.Top is { } top
            && left >= SystemParameters.VirtualScreenLeft
            && top >= SystemParameters.VirtualScreenTop
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    void AddCamera_Click(object sender, RoutedEventArgs e) => AddCamera(null);

    void AddCamera(Camera? prefilled)
    {
        var dialog = new AddCameraDialog(prefilled) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var camera = dialog.Result;
        camera.Order = _cameras.Count == 0 ? 0 : _cameras.Max(c => c.Order) + 1;
        _cameras.Add(camera);
        SaveCameras();
        RebuildView();
    }

    void Discover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiscoveryDialog(_cameras.Select(c => c.Host)) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } found) AddCamera(found);
    }

    void EditCamera(CameraTile tile)
    {
        var dialog = new AddCameraDialog(tile.Camera, isNew: false) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var updated = dialog.Result;
        // The URL may change: never keep recording from the old one.
        if (_recordings.IsRecording(updated.Id)) _recordings.StopWithNotice(updated.Id);
        _cameras[_cameras.FindIndex(c => c.Id == updated.Id)] = updated;
        // New settings (often a corrected password) deserve fresh notices.
        _errors.Forget(updated.Id);
        DisposeTilesOf(updated.Id);
        SaveCameras();
        RebuildView();
    }

    /// <summary>Opens «Añadir cámara» pre-filled with a copy (new Id, "(copia)"); nothing is saved unless the user saves.</summary>
    void DuplicateCamera(CameraTile tile)
    {
        if (_cameras.FirstOrDefault(c => c.Id == tile.Camera.Id) is { } camera) AddCamera(camera.Duplicate());
    }

    void DeleteCamera(CameraTile tile)
    {
        var answer = MessageBox.Show(this, $"¿Eliminar la cámara «{tile.Camera.Name}»?", "Centinela",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        if (_recordings.IsRecording(tile.Camera.Id)) _recordings.StopWithNotice(tile.Camera.Id);
        _cameras.RemoveAll(c => c.Id == tile.Camera.Id);
        _errors.Forget(tile.Camera.Id);
        DisposeTilesOf(tile.Camera.Id);
        SaveCameras();
        if (_settings.FeaturedCameraId == tile.Camera.Id)
        {
            _settings.FeaturedCameraId = null;
            SaveSettingsQuietly();
        }
        if (_settings.DualCameraIds.Remove(tile.Camera.Id)) SaveSettingsQuietly();
        RebuildView();
    }

    internal void Notify(string message, string? revealPath)
    {
        StatusText.Text = message;
        _statusRevealPath = revealPath;
    }

    void StatusText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_statusRevealPath is null) return;
        if (Directory.Exists(_statusRevealPath)) Process.Start("explorer.exe", $"\"{_statusRevealPath}\"");
        else if (File.Exists(_statusRevealPath)) Process.Start("explorer.exe", $"/select,\"{_statusRevealPath}\"");
    }

    void OpenRecordings_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        Process.Start("explorer.exe", $"\"{AppPaths.RecordingsDirectory}\"");
    }

    /// <summary>
    /// Keeps a tile shutdown in <see cref="_pendingShutdowns"/> until it completes, then reports any
    /// failure. Call on the UI thread.
    /// </summary>
    void TrackShutdown(Task shutdown, string name)
    {
        _pendingShutdowns.Add(shutdown);
        shutdown.ContinueWith(done => Dispatcher.BeginInvoke(() =>
        {
            _pendingShutdowns.Remove(shutdown);
            if (done.Exception?.GetBaseException().Message is { } error)
                Notify($"Error al detener la cámara {name}: {error}", null);
        }), TaskScheduler.Default);
    }

    void SaveCameras()
    {
        try
        {
            _store.Save(_cameras);
        }
        catch (Exception ex)
        {
            Notify($"No se pudieron guardar las cámaras: {ex.Message}", null);
            return;
        }
        ScheduleAutomaticBackup();
    }

    void SaveSettingsQuietly()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception)
        {
            // Losing a preference (window placement, layout) must never interrupt the user or block closing.
        }
    }

    /// <summary>Leaves F11 fullscreen and saves the window's bounds; does nothing while the window is hidden.</summary>
    void SaveWindowPlacement()
    {
        if (!IsVisible) return;
        if (WindowStyle == WindowStyle.None) ToggleFullscreen();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Maximized = WindowState == WindowState.Maximized;
        SaveSettingsQuietly();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            // The X hides to the tray; recordings keep running.
            e.Cancel = true;
            HideToTray();
            return;
        }
        SaveWindowPlacement();
        // Close fullscreen views now so their shutdowns are tracked before OnClosed waits.
        foreach (var owned in OwnedWindows.Cast<Window>().ToList()) owned.Close();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        // Tiles and recordings shut down in parallel; one bounded wait so recordings get their trailer.
        var shutdowns = _tiles.Values.Concat(_placeholders.Values).Select(tile => tile.ShutdownAsync())
            .Concat(_pendingShutdowns)
            .Append(_recordings.ShutdownAsync())
            .Append(AutomaticBackupAsync())
            .Append(_manualExport)
            .ToArray();
        _tiles.Clear();
        _placeholders.Clear();
        // The tiles above released their leases (the last one stops each session, awaited below); stop whatever
        // else still holds a shared session now rather than after the wait, so it winds down in parallel.
        _streams.Dispose();
        try
        {
            Task.WaitAll(shutdowns, TimeSpan.FromSeconds(8));
        }
        catch (AggregateException)
        {
            // Failures were already reported or cannot be shown any more; exit anyway.
        }
        _audioOutput?.Dispose();
        _audioOutput = null;
        _tray.Dispose();
        _errorLog.Dispose();
        base.OnClosed(e);
        // ShutdownMode is OnExplicitShutdown: closing the window alone would leave the process running.
        Application.Current.Shutdown();
    }
}
