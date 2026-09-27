using System.Windows;
using System.Windows.Controls;
using CamaraWin.Core;

namespace CamaraWin.App;

/// <summary>The tile view: grid or featured layout planned by <see cref="ViewPlanner"/>.</summary>
public partial class MainWindow
{
    readonly Dictionary<(Guid Id, StreamKind Kind), CameraTile> _tiles = [];
    // Old-stream tiles kept visible over a new tile until the new one shows its first frame.
    readonly Dictionary<CameraTile, CameraTile> _placeholders = [];   // new tile → placeholder

    const int FeaturedIndex = 5;

    ViewPlan PlanView() =>
        ViewPlanner.Plan(_cameras, _settings.LayoutMode, _settings.GridMode, _settings.FeaturedCameraId);

    /// <summary>The camera shown large in featured mode (null in grid mode or without cameras).</summary>
    Guid? FeaturedCameraId() =>
        _settings.LayoutMode == LayoutMode.Featured && PlanView().Slots is [var first, ..] ? first.CameraId : null;

    void RebuildView()
    {
        var plan = PlanView();
        TileGrid.RowDefinitions.Clear();
        TileGrid.ColumnDefinitions.Clear();
        for (var r = 0; r < plan.Rows; r++) TileGrid.RowDefinitions.Add(new RowDefinition());
        for (var c = 0; c < plan.Columns; c++) TileGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var wanted = plan.Slots.Select(s => (s.CameraId, s.Kind)).ToHashSet();
        foreach (var slot in plan.Slots) PlaceTile(slot, wanted);
        foreach (var key in _tiles.Keys.Where(k => !wanted.Contains(k)).ToList()) DisposeTile(key);
        EmptyState.Visibility = _cameras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void PlaceTile(TileSlot slot, HashSet<(Guid, StreamKind)> wanted)
    {
        var key = (slot.CameraId, slot.Kind);
        if (_tiles.TryGetValue(key, out var existing))
        {
            Position(existing, slot);
            if (_placeholders.TryGetValue(existing, out var covering)) Cover(covering, slot);
            return;
        }
        var camera = _cameras.First(c => c.Id == slot.CameraId);
        var tile = CreateTile(camera, slot.Kind);
        _tiles[key] = tile;
        Position(tile, slot);
        TileGrid.Children.Add(tile);

        // Same camera already on screen with the other stream (e.g. just promoted/demoted): keep showing
        // it on top of the new tile until the new stream has a frame, so the swap never flashes black.
        var otherKey = (slot.CameraId, slot.Kind == StreamKind.Main ? StreamKind.Sub : StreamKind.Main);
        if (!wanted.Contains(otherKey) && _tiles.Remove(otherKey, out var placeholder))
        {
            // Swapped back before the other stream painted: keep the older tile, which is the one on screen.
            if (_placeholders.Remove(placeholder, out var older))
            {
                placeholder.FirstFrameShown -= RetirePlaceholder;
                RemoveAndShutdown(placeholder);
                placeholder = older;
            }
            Cover(placeholder, slot);
            _placeholders[tile] = placeholder;
            tile.FirstFrameShown += RetirePlaceholder;
        }
    }

    CameraTile CreateTile(Camera camera, StreamKind kind)
    {
        var tile = new CameraTile(camera, kind);
        tile.EditRequested += EditCamera;
        tile.DeleteRequested += DeleteCamera;
        tile.Notify += Notify;
        tile.FullscreenRequested += ShowFullscreen;
        tile.SwapRequested += SwapCameras;
        tile.RecordRequested += t => ToggleRecording(t.Camera);
        tile.Clicked += FeatureCamera;
        tile.SetRecordingStatus(_recordings.StatusOf(camera.Id));
        return tile;
    }

    void RetirePlaceholder(CameraTile tile)
    {
        tile.FirstFrameShown -= RetirePlaceholder;
        if (_placeholders.Remove(tile, out var placeholder)) RemoveAndShutdown(placeholder);
    }

    static void Position(CameraTile tile, TileSlot slot)
    {
        Grid.SetRow(tile, slot.Row);
        Grid.SetColumn(tile, slot.Column);
        Grid.SetRowSpan(tile, slot.RowSpan);
        Grid.SetColumnSpan(tile, slot.ColumnSpan);
        Panel.SetZIndex(tile, 0);
    }

    static void Cover(CameraTile placeholder, TileSlot slot)
    {
        Position(placeholder, slot);
        Panel.SetZIndex(placeholder, 1);
    }

    /// <summary>Removes the tile at once; its live session stops in the background (recordings are unaffected).</summary>
    void DisposeTile((Guid Id, StreamKind Kind) key)
    {
        if (!_tiles.Remove(key, out var tile)) return;
        if (_placeholders.Remove(tile, out var placeholder)) RemoveAndShutdown(placeholder);
        RemoveAndShutdown(tile);
    }

    void DisposeTilesOf(Guid cameraId)
    {
        foreach (var key in _tiles.Keys.Where(k => k.Id == cameraId).ToList()) DisposeTile(key);
    }

    void RemoveAndShutdown(CameraTile tile)
    {
        TileGrid.Children.Remove(tile);
        TrackShutdown(tile.ShutdownAsync(), tile.Camera.Name);
    }

    void FeatureCamera(CameraTile tile)
    {
        if (_settings.LayoutMode != LayoutMode.Featured || tile.Kind == StreamKind.Main) return;
        _settings.FeaturedCameraId = tile.Camera.Id;
        SaveSettingsQuietly();
        RebuildView();
    }
}
