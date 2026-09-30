using Avalonia.Controls;
using Avalonia.Threading;
using Centinela.Core;
using Centinela.Desktop.Services.Layout;
using Centinela.Desktop.ViewModels;
using Centinela.Desktop.Views;

namespace Centinela.Desktop.Services.Tiles;

/// <summary>
/// Puts the planned tiles in the grid: reuses a tile that already shows the same camera and stream, creates the
/// missing ones and retires the rest. When a camera switches stream (promoted/demoted), the old tile stays on top
/// of the new one until the new one paints, fails or 5 s pass, so the switch never flashes black. UI thread only.
/// </summary>
sealed class TileHost(Grid grid, TileFactory factory, Func<Guid, Camera?> findCamera)
{
    static readonly TimeSpan PlaceholderTimeout = TimeSpan.FromSeconds(5);
    readonly Dictionary<(Guid Id, StreamKind Kind), CameraTileView> _tiles = [];
    readonly Dictionary<CameraTileView, CameraTileView> _placeholders = []; // new tile → placeholder

    /// <summary>A tile's shutdown began (task, camera name); the owner waits for these on exit.</summary>
    public event Action<Task, string>? ShutdownStarted;

    public IEnumerable<CameraTileView> All => _tiles.Values.Concat(_placeholders.Values);

    public IEnumerable<CameraTileView> TilesOf(Guid cameraId) => All.Where(t => t.ViewModel.Camera.Id == cameraId);

    public void Apply(LayoutPlan plan)
    {
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        for (var r = 0; r < plan.Rows; r++) grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        for (var c = 0; c < plan.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var wanted = plan.Tiles.Select(t => (t.CameraId, t.Stream)).ToHashSet();
        foreach (var tile in plan.Tiles) Place(tile, wanted);
        foreach (var key in _tiles.Keys.Where(k => !wanted.Contains(k)).ToList()) DisposeTile(key);
    }

    void Place(PlacedTile slot, HashSet<(Guid, StreamKind)> wanted)
    {
        var key = (slot.CameraId, slot.Stream);
        var otherKey = (slot.CameraId, slot.Stream == StreamKind.Main ? StreamKind.Sub : StreamKind.Main);
        if (_tiles.TryGetValue(key, out var existing))
        {
            existing.ViewModel.Big = slot.Big;
            Position(existing, slot, z: 0);
            if (_placeholders.TryGetValue(existing, out var covering)) Position(covering, slot, z: 1);
            return;
        }
        var otherWanted = wanted.Contains(otherKey);

        // Swapped back before the other stream painted: the tile still on screen is this very stream.
        if (!otherWanted && _tiles.TryGetValue(otherKey, out var unpainted)
            && _placeholders.TryGetValue(unpainted, out var previous) && previous.ViewModel.Kind == slot.Stream)
        {
            _placeholders.Remove(unpainted);
            _tiles.Remove(otherKey);
            RemoveAndShutdown(unpainted);
            _tiles[key] = previous;
            previous.ViewModel.Big = slot.Big;
            Position(previous, slot, z: 0);
            return;
        }

        if (findCamera(slot.CameraId) is not { } camera) return;
        var tile = factory.Create(camera, slot.Stream, slot.Big);
        _tiles[key] = tile;
        Position(tile, slot, z: 0);
        grid.Children.Add(tile);

        // Same camera already on screen with the other stream: keep it over the new tile until that one has a frame.
        if (!otherWanted && _tiles.Remove(otherKey, out var placeholder))
        {
            if (tile.ViewModel.Session is null)
            {
                RemoveAndShutdown(placeholder);
                return;
            }
            Position(placeholder, slot, z: 1);
            _placeholders[tile] = placeholder;
            tile.FirstFrameShown += Retire;
            tile.ViewModel.StreamFailed += RetireFailed;
            var timeout = new DispatcherTimer { Interval = PlaceholderTimeout };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                Retire(tile);
            };
            timeout.Start();
        }
    }

    void Retire(CameraTileView tile)
    {
        tile.FirstFrameShown -= Retire;
        tile.ViewModel.StreamFailed -= RetireFailed;
        if (_placeholders.Remove(tile, out var placeholder)) RemoveAndShutdown(placeholder);
    }

    // Named (not a lambda) so Retire can unsubscribe it, as the WPF RetirePlaceholder did.
    void RetireFailed(TileViewModel failed)
    {
        if (_placeholders.Keys.FirstOrDefault(t => t.ViewModel == failed) is { } tile) Retire(tile);
    }

    static void Position(Control tile, PlacedTile slot, int z)
    {
        Grid.SetRow(tile, slot.Row);
        Grid.SetColumn(tile, slot.Column);
        Grid.SetRowSpan(tile, slot.RowSpan);
        Grid.SetColumnSpan(tile, slot.ColumnSpan);
        tile.ZIndex = z;
    }

    void DisposeTile((Guid, StreamKind) key)
    {
        if (!_tiles.Remove(key, out var tile)) return;
        if (_placeholders.Remove(tile, out var placeholder)) RemoveAndShutdown(placeholder);
        RemoveAndShutdown(tile);
    }

    /// <summary>Removes every tile of the camera (edited or deleted camera).</summary>
    public void DisposeTilesOf(Guid cameraId)
    {
        foreach (var key in _tiles.Keys.Where(k => k.Id == cameraId).ToList()) DisposeTile(key);
    }

    void RemoveAndShutdown(CameraTileView tile)
    {
        grid.Children.Remove(tile);
        ShutdownStarted?.Invoke(tile.ViewModel.ShutdownAsync(), tile.ViewModel.Camera.Name);
    }

    /// <summary>Stops every tile (exit); returns their shutdowns.</summary>
    public IReadOnlyList<Task> ShutdownAll()
    {
        var shutdowns = All.Select(t => t.ViewModel.ShutdownAsync()).ToList();
        _tiles.Clear();
        _placeholders.Clear();
        grid.Children.Clear();
        return shutdowns;
    }
}
