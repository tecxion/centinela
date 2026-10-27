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
    readonly Dictionary<CameraTileView, DispatcherTimer> _timeouts = []; // new tile → its placeholder's 5 s limit

    /// <summary>A tile's shutdown began (task, camera name); the owner waits for these on exit.</summary>
    public event Action<Task, string>? ShutdownStarted;

    /// <summary>
    /// The tile must stop sounding: it leaves the grid, is no longer big, or only covers its replacement. Raised
    /// before the tile changes.
    /// </summary>
    public event Action<TileViewModel>? AudioReleaseRequested;

    public IEnumerable<CameraTileView> All => _tiles.Values.Concat(_placeholders.Values);

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
            SetBig(existing, slot.Big);
            Position(existing, slot, z: 0);
            if (_placeholders.TryGetValue(existing, out var covering)) Position(covering, slot, z: 1);
            return;
        }
        var otherWanted = wanted.Contains(otherKey);

        // Swapped back before the other stream painted: the tile still on screen is this very stream.
        if (!otherWanted && _tiles.TryGetValue(otherKey, out var unpainted)
            && _placeholders.TryGetValue(unpainted, out var previous) && previous.ViewModel.Kind == slot.Stream)
        {
            Unlink(unpainted);
            _tiles.Remove(otherKey);
            RemoveAndShutdown(unpainted);
            _tiles[key] = previous;
            SetBig(previous, slot.Big);
            // Zoom was turned off while it covered; Big may not have changed, so set it back explicitly.
            previous.ViewModel.EnableZoom = slot.Big;
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
            // No session, or already failing when created (a shared session that was reconnecting: its StreamFailed
            // was raised inside Start, before this could subscribe): nothing to wait for.
            if (tile.ViewModel.Session is null || tile.ViewModel.IsFailing)
            {
                RemoveAndShutdown(placeholder);
                return;
            }
            // A demoted camera must not keep sounding from under the thumbnail that replaces it.
            AudioReleaseRequested?.Invoke(placeholder.ViewModel);
            // It only covers the new tile until that one has a frame: not zoomed, and no zoom.
            placeholder.ResetZoom();
            placeholder.ViewModel.EnableZoom = false;
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
            _timeouts[tile] = timeout;
            timeout.Start();
        }
    }

    void Retire(CameraTileView tile)
    {
        if (Unlink(tile) is { } placeholder) RemoveAndShutdown(placeholder);
    }

    /// <summary>Stops waiting for <paramref name="tile"/> to paint; returns the placeholder that covered it, if any.</summary>
    CameraTileView? Unlink(CameraTileView tile)
    {
        tile.FirstFrameShown -= Retire;
        tile.ViewModel.StreamFailed -= RetireFailed;
        if (_timeouts.Remove(tile, out var timeout)) timeout.Stop();
        return _placeholders.Remove(tile, out var placeholder) ? placeholder : null;
    }

    // Named (not a lambda) so Retire can unsubscribe it, as the WPF RetirePlaceholder did.
    void RetireFailed(TileViewModel failed)
    {
        if (_placeholders.Keys.FirstOrDefault(t => t.ViewModel == failed) is { } tile) Retire(tile);
    }

    /// <summary>
    /// Big tiles zoom and, on their own main-stream session, may play audio. A tile that stays on screen as a
    /// thumbnail drops the audio (its zoom is reset by the view).
    /// </summary>
    void SetBig(CameraTileView tile, bool big)
    {
        var viewModel = tile.ViewModel;
        if (!big) AudioReleaseRequested?.Invoke(viewModel);
        viewModel.Big = big;
        // Shared (substream) sessions never play audio: the sink would reach every viewer of the lease.
        viewModel.AudioCapable = big && viewModel.Kind == StreamKind.Main;
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
        if (Unlink(tile) is { } placeholder) RemoveAndShutdown(placeholder);
        RemoveAndShutdown(tile);
    }

    /// <summary>Removes every grid tile of the camera (edited or deleted camera); the window closes its fullscreen views.</summary>
    public void DisposeTilesOf(Guid cameraId)
    {
        foreach (var key in _tiles.Keys.Where(k => k.Id == cameraId).ToList()) DisposeTile(key);
    }

    void RemoveAndShutdown(CameraTileView tile)
    {
        AudioReleaseRequested?.Invoke(tile.ViewModel);
        grid.Children.Remove(tile);
        ShutdownStarted?.Invoke(tile.ViewModel.ShutdownAsync(), tile.ViewModel.Camera.Name);
    }

    /// <summary>Removes every tile (hidden in the tray); their shutdowns are reported through <see cref="ShutdownStarted"/>.</summary>
    public void RetireAll()
    {
        foreach (var key in _tiles.Keys.ToList()) DisposeTile(key);
    }

    /// <summary>Stops every tile (exit); returns their shutdowns.</summary>
    public IReadOnlyList<Task> ShutdownAll()
    {
        var shutdowns = All.Select(t => t.ViewModel.ShutdownAsync()).ToList();
        _tiles.Clear();
        _placeholders.Clear();
        foreach (var timeout in _timeouts.Values) timeout.Stop();
        _timeouts.Clear();
        grid.Children.Clear();
        return shutdowns;
    }
}
