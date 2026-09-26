using System.Diagnostics;
using System.IO;
using System.Windows;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class MainWindow : Window
{
    readonly CameraStore _store = new(CameraStore.DefaultPath);
    readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath);
    readonly AppSettings _settings;
    readonly List<Camera> _cameras;
    readonly Dictionary<Guid, CameraTile> _tiles = [];
    string? _statusRevealPath;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        _cameras = [.. _store.Load()];
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;
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
        return tile;
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

    void EditCamera(CameraTile tile)
    {
        var dialog = new AddCameraDialog(tile.Camera, isNew: false) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var updated = dialog.Result;
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

    void DisposeTile(Guid id)
    {
        if (!_tiles.Remove(id, out var tile)) return;
        TileGrid.Children.Remove(tile);
        tile.Dispose();
    }

    void SaveCameras() => _store.Save(_cameras);

    protected override void OnClosed(EventArgs e)
    {
        foreach (var tile in _tiles.Values) tile.RequestStop();
        foreach (var tile in _tiles.Values) tile.Dispose();
        base.OnClosed(e);
    }
}
