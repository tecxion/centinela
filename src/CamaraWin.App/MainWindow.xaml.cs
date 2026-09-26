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

    CameraTile CreateTile(Camera camera) => new(camera, StreamKind.Sub);

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
