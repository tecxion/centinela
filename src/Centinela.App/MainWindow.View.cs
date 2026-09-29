using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Centinela.Core;

namespace Centinela.App;

/// <summary>The tile view: grid or featured layout planned by <see cref="ViewPlanner"/>.</summary>
public partial class MainWindow
{
    readonly Dictionary<(Guid Id, StreamKind Kind), CameraTile> _tiles = [];
    // Old-stream tiles kept visible over a new tile until the new one shows its first frame.
    readonly Dictionary<CameraTile, CameraTile> _placeholders = [];   // new tile → placeholder

    // Combo indexes 0..4 are the grid modes; the rest are these layouts, in combo order.
    static readonly LayoutMode[] LayoutModes = [LayoutMode.Featured, LayoutMode.FeaturedLeft, LayoutMode.Dual];
    // A placeholder never outlives this, even if the new stream neither paints nor reports a failure.
    static readonly TimeSpan PlaceholderTimeout = TimeSpan.FromSeconds(5);

    int ComboIndexForSettings() =>
        _settings.LayoutMode == LayoutMode.Grid
            ? Math.Max(0, Array.IndexOf(GridModes, _settings.GridMode))
            : GridModes.Length + Array.IndexOf(LayoutModes, _settings.LayoutMode);

    ViewPlan PlanView() =>
        ViewPlanner.Plan(_cameras, _settings.LayoutMode, _settings.GridMode, _settings.FeaturedCameraId, _settings.DualCameraIds);

    bool IsFeaturedLayout => _settings.LayoutMode is LayoutMode.Featured or LayoutMode.FeaturedLeft;

    /// <summary>The cameras shown big (Main) in the current layout, in plan order (dual: left, right).</summary>
    IReadOnlyList<Guid> BigCameraIds() =>
        PlanView().Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToList();

    /// <summary>The camera shown large in a featured layout (null otherwise or without cameras).</summary>
    Guid? FeaturedCameraId() => IsFeaturedLayout && BigCameraIds() is [var first, ..] ? first : null;

    void RebuildView()
    {
        var plan = PlanView();
        TileGrid.RowDefinitions.Clear();
        TileGrid.ColumnDefinitions.Clear();
        for (var r = 0; r < plan.Rows; r++) TileGrid.RowDefinitions.Add(new RowDefinition());
        for (var c = 0; c < plan.Columns; c++) TileGrid.ColumnDefinitions.Add(new ColumnDefinition());

        // The plan marks big slots as Main; low-quality cameras play them on the substream instead.
        var bigs = plan.Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToHashSet();
        var slots = plan.Slots.Select(s => s with { Kind = ViewPlanner.StreamFor(_cameras.First(c => c.Id == s.CameraId), s.Kind) }).ToList();
        var wanted = slots.Select(s => (s.CameraId, s.Kind)).ToHashSet();
        foreach (var slot in slots) PlaceTile(slot, bigs.Contains(slot.CameraId), wanted);
        foreach (var key in _tiles.Keys.Where(k => !wanted.Contains(k)).ToList()) DisposeTile(key);
        EmptyState.Visibility = _cameras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void PlaceTile(TileSlot slot, bool big, HashSet<(Guid, StreamKind)> wanted)
    {
        var key = (slot.CameraId, slot.Kind);
        if (_tiles.TryGetValue(key, out var existing))
        {
            SetBig(existing, big);
            Position(existing, slot);
            if (_placeholders.TryGetValue(existing, out var covering)) Cover(covering, slot);
            return;
        }
        var otherKey = (slot.CameraId, slot.Kind == StreamKind.Main ? StreamKind.Sub : StreamKind.Main);
        var otherWanted = wanted.Contains(otherKey);

        // Swapped back before the other stream painted: the tile still on screen is this very stream,
        // so bring it back instead of opening a second connection to the camera.
        if (!otherWanted && _tiles.TryGetValue(otherKey, out var unpainted)
            && _placeholders.TryGetValue(unpainted, out var previous) && previous.Kind == slot.Kind)
        {
            _placeholders.Remove(unpainted);
            _tiles.Remove(otherKey);
            RemoveAndShutdown(unpainted);
            _tiles[key] = previous;
            SetBig(previous, big);
            Position(previous, slot);
            return;
        }

        var camera = _cameras.First(c => c.Id == slot.CameraId);
        var tile = CreateTile(camera, slot.Kind, big);
        _tiles[key] = tile;
        Position(tile, slot);
        TileGrid.Children.Add(tile);

        // Same camera already on screen with the other stream (e.g. just promoted/demoted): keep showing
        // it on top of the new tile until the new stream has a frame, so the swap never flashes black.
        // It gives way as soon as the new stream fails or the timeout passes, so it never hides the new tile for good.
        if (!otherWanted && _tiles.Remove(otherKey, out var placeholder))
        {
            if (!tile.HasStream)
            {
                RemoveAndShutdown(placeholder);
                return;
            }
            // A demoted camera must not keep sounding from under the thumbnail that replaces it.
            ReleaseAudio(placeholder);
            // Nor stay zoomed: it only covers the new tile until that one has a frame.
            placeholder.ResetZoom();
            placeholder.EnableZoom = false;
            Cover(placeholder, slot);
            _placeholders[tile] = placeholder;
            tile.FirstFrameShown += RetirePlaceholder;
            tile.StreamFailed += RetirePlaceholder;
            var timeout = new DispatcherTimer { Interval = PlaceholderTimeout };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                RetirePlaceholder(tile);
            };
            timeout.Start();
        }
    }

    CameraTile CreateTile(Camera camera, StreamKind kind, bool big)
    {
        // Substream tiles share one connection per camera (the tile sets its decode size once laid out).
        var tile = kind == StreamKind.Sub
            ? new CameraTile(camera, kind, acquireShared: c => AcquireSub(c, 0, 0))
            : new CameraTile(camera, kind);
        SetBig(tile, big);
        tile.SetLowQuality(camera.LowQualityWhenBig);
        tile.QualityToggleRequested += ToggleQuality;
        tile.SetSmoothing(camera.Smoothing);
        tile.SmoothingToggleRequested += ToggleSmoothing;
        WireAudio(tile);
        tile.EditRequested += EditCamera;
        tile.DuplicateRequested += DuplicateCamera;
        tile.DeleteRequested += DeleteCamera;
        tile.Notify += Notify;
        tile.FullscreenRequested += ShowFullscreen;
        tile.SwapRequested += SwapCameras;
        tile.RecordRequested += t => ToggleRecording(t.Camera);
        tile.Clicked += FeatureCamera;
        tile.SetRecordingStatus(_recordings.StatusOf(camera.Id));
        tile.ShowStats = _settings.ShowStats;
        WireErrors(tile);
        WireMotion(tile);
        return tile;
    }

    /// <summary>
    /// Big tiles (featured/dual) zoom and, on their own main-stream session, may play audio. A tile that stays
    /// on screen as a thumbnail (a low-quality camera's substream losing the big slot) drops both.
    /// </summary>
    void SetBig(CameraTile tile, bool big)
    {
        if (tile.Big == big && tile.EnableZoom == big) return;
        if (!big)
        {
            tile.ResetZoom();
            ReleaseAudio(tile);
        }
        tile.Big = big;
        tile.EnableZoom = big;
        // Shared (substream) sessions never play audio: the sink would reach every viewer of the lease.
        tile.AudioCapable = big && tile.Kind == StreamKind.Main;
    }

    /// <summary>The HD/SD button: switches the camera's big view between main and substream, and remembers it.</summary>
    void ToggleQuality(CameraTile shown)
    {
        if (_cameras.FirstOrDefault(c => c.Id == shown.Camera.Id) is not { } camera) return;
        camera.LowQualityWhenBig = !camera.LowQualityWhenBig;
        SaveCameras();
        foreach (var tile in TilesOf(camera.Id)) tile.SetLowQuality(camera.LowQualityWhenBig);
        RebuildView();
    }

    /// <summary>
    /// «Suavizar imagen»: flips the camera's smoothing, remembers it and applies it live to every session
    /// showing it (own main-stream sessions through their tiles, shared substream sessions directly).
    /// </summary>
    void ToggleSmoothing(CameraTile shown)
    {
        if (_cameras.FirstOrDefault(c => c.Id == shown.Camera.Id) is not { } camera) return;
        camera.Smoothing = !camera.Smoothing;
        SaveCameras();
        foreach (var tile in TilesOf(camera.Id)) tile.SetSmoothing(camera.Smoothing);
        foreach (var session in _streams.SessionsFor(camera.Id)) session.Smoothing = camera.Smoothing;
    }

    void RetirePlaceholder(CameraTile tile)
    {
        tile.FirstFrameShown -= RetirePlaceholder;
        tile.StreamFailed -= RetirePlaceholder;
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

    /// <summary>
    /// Removes every view of the camera, including fullscreen windows (they hold the old <see cref="Camera"/>
    /// object and would keep showing, and recording, the old settings). Their shutdowns are tracked on close.
    /// </summary>
    void DisposeTilesOf(Guid cameraId)
    {
        foreach (var key in _tiles.Keys.Where(k => k.Id == cameraId).ToList()) DisposeTile(key);
        foreach (var window in OwnedWindows.OfType<FullscreenWindow>().Where(w => w.Tile.Camera.Id == cameraId).ToList())
            window.Close();
    }

    void RemoveAndShutdown(CameraTile tile)
    {
        ReleaseAudio(tile);
        TileGrid.Children.Remove(tile);
        TrackShutdown(tile.ShutdownAsync(), tile.Camera.Name);
    }

    void FeatureCamera(CameraTile tile)
    {
        if (IsFeaturedLayout)
        {
            if (tile.Camera.Id == FeaturedCameraId()) return;
            _settings.FeaturedCameraId = tile.Camera.Id;
        }
        else if (_settings.LayoutMode == LayoutMode.Dual)
        {
            var current = new DualState(BigCameraIds(), _settings.DualNextReplace);
            var next = DualSelection.Click(current, tile.Camera.Id);
            if (ReferenceEquals(next, current)) return;
            StoreDual(next);
        }
        else return;
        SaveSettingsQuietly();
        RebuildView();
    }

    void StoreDual(DualState state)
    {
        _settings.DualCameraIds = [.. state.Ids];
        _settings.DualNextReplace = state.NextReplace;
    }
}
