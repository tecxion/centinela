using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class MainWindow : Window
{
    readonly CameraStore _store = new(CameraStore.DefaultPath);
    readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath);
    readonly AppSettings _settings;
    readonly List<Camera> _cameras;
    readonly Dictionary<Guid, CameraTile> _tiles = [];
    readonly RecordingController _recordings;
    // Shutdowns of tiles removed from the grid; OnClosed waits for them too (UI thread only).
    readonly List<Task> _pendingShutdowns = [];
    string? _statusRevealPath;
    static readonly GridMode[] GridModes = [GridMode.Auto, GridMode.One, GridMode.Four, GridMode.Nine, GridMode.Sixteen];
    WindowState _stateBeforeFullscreen;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        _cameras = [.. _store.Load()];
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;

        RestoreWindowPlacement();
        GridModeBox.SelectedIndex = Math.Max(0, Array.IndexOf(GridModes, _settings.GridMode));
        GridModeBox.SelectionChanged += GridMode_Changed;
        _recordings = new RecordingController(Dispatcher);
        _recordings.Notify += Notify;
        _recordings.StatusChanged += (id, status) =>
        {
            foreach (var tile in TilesOf(id)) tile.SetRecordingStatus(status);
        };
        RebuildGrid();
    }

    void RebuildGrid()
    {
        var ordered = _cameras.OrderBy(c => c.Order).ToList();
        var visible = ordered.Take(GridLayout.VisibleCount(ordered.Count, _settings.GridMode)).ToList();
        var size = GridLayout.Compute(visible.Count, _settings.GridMode);

        foreach (var id in _tiles.Keys.Except(visible.Select(c => c.Id)).ToList()) DisposeTile(id);

        TileGrid.Children.Clear();
        TileGrid.Rows = size.Rows;
        TileGrid.Columns = size.Columns;
        foreach (var camera in visible)
        {
            if (!_tiles.TryGetValue(camera.Id, out var tile))
            {
                tile = CreateTile(camera);
                _tiles[camera.Id] = tile;
            }
            TileGrid.Children.Add(tile);
        }
        EmptyState.Visibility = _cameras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    CameraTile CreateTile(Camera camera)
    {
        var tile = new CameraTile(camera, StreamKind.Sub);
        tile.EditRequested += EditCamera;
        tile.DeleteRequested += DeleteCamera;
        tile.Notify += Notify;
        tile.FullscreenRequested += ShowFullscreen;
        tile.SwapRequested += SwapCameras;
        tile.RecordRequested += t => ToggleRecording(t.Camera);
        tile.SetRecordingStatus(_recordings.StatusOf(camera.Id));
        return tile;
    }

    /// <summary>Every tile showing this camera: its grid tile plus any open fullscreen view.</summary>
    IEnumerable<CameraTile> TilesOf(Guid id)
    {
        if (_tiles.TryGetValue(id, out var tile)) yield return tile;
        foreach (var window in OwnedWindows.OfType<FullscreenWindow>())
            if (window.Tile.Camera.Id == id) yield return window.Tile;
    }

    void ToggleRecording(Camera camera)
    {
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
        // OnClosed waits for the live session's shutdown like any other.
        window.Closed += (_, _) => TrackShutdown(window.Tile.ShutdownAsync(), name);
        window.Show();
    }

    void SwapCameras(Guid source, Guid target)
    {
        var a = _cameras.FirstOrDefault(c => c.Id == source);
        var b = _cameras.FirstOrDefault(c => c.Id == target);
        if (a is null || b is null) return;
        (a.Order, b.Order) = (b.Order, a.Order);
        SaveCameras();
        RebuildGrid();
    }

    void GridMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        _settings.GridMode = GridModes[GridModeBox.SelectedIndex];
        RebuildGrid();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None) ToggleFullscreen();
    }

    void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _stateBeforeFullscreen;
            TopBar.Visibility = BottomBar.Visibility = Visibility.Visible;
        }
        else
        {
            _stateBeforeFullscreen = WindowState;
            TopBar.Visibility = BottomBar.Visibility = Visibility.Collapsed;
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
        RebuildGrid();
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
        DisposeTile(updated.Id);
        SaveCameras();
        RebuildGrid();
    }

    void DeleteCamera(CameraTile tile)
    {
        var answer = MessageBox.Show(this, $"¿Eliminar la cámara «{tile.Camera.Name}»?", "CamaraWin",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        if (_recordings.IsRecording(tile.Camera.Id)) _recordings.StopWithNotice(tile.Camera.Id);
        _cameras.RemoveAll(c => c.Id == tile.Camera.Id);
        DisposeTile(tile.Camera.Id);
        SaveCameras();
        RebuildGrid();
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

    /// <summary>Removes the tile at once; its live session stops in the background (recordings are unaffected).</summary>
    void DisposeTile(Guid id)
    {
        if (!_tiles.Remove(id, out var tile)) return;
        TileGrid.Children.Remove(tile);
        TrackShutdown(tile.ShutdownAsync(), tile.Camera.Name);
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
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (WindowStyle == WindowStyle.None) ToggleFullscreen();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Maximized = WindowState == WindowState.Maximized;
        // Close fullscreen views now so their shutdowns are tracked before OnClosed waits.
        foreach (var owned in OwnedWindows.Cast<Window>().ToList()) owned.Close();
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception)
        {
            // Losing the window placement must not block closing (the tiles still need to shut down).
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Tiles and recordings shut down in parallel; one bounded wait so recordings get their trailer.
        var shutdowns = _tiles.Values.Select(tile => tile.ShutdownAsync())
            .Concat(_pendingShutdowns)
            .Append(_recordings.ShutdownAsync())
            .ToArray();
        _tiles.Clear();
        try
        {
            Task.WaitAll(shutdowns, TimeSpan.FromSeconds(8));
        }
        catch (AggregateException)
        {
            // Failures were already reported or cannot be shown any more; exit anyway.
        }
        base.OnClosed(e);
    }
}
