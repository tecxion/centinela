using Centinela.Core;
using Centinela.Desktop.Services.Cameras;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

/// <summary>
/// HD/SD, «Suavizar imagen» and 👁 on the stored camera, saved and shown on every tile. Cameras have no URL, so tiles
/// have no session (no network); files go to a temp folder.
/// </summary>
public sealed class CameraActionsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());
    readonly StreamLeases _leases = new();
    readonly CameraRepository _cameras;
    readonly Camera _camera = new() { Name = "Patio", Brand = Brand.Custom };
    readonly Camera _other = new() { Name = "Garaje", Brand = Brand.Custom };
    readonly List<TileViewModel> _tiles = [];
    readonly CameraActions _actions;
    int _saves;

    public CameraActionsTests()
    {
        _cameras = new CameraRepository(new CameraStore(Path.Combine(_dir, "cameras.json"), new FakeSecretStore()));
        _cameras.Load();
        _cameras.Add(_camera);
        _cameras.Add(_other);
        _cameras.Saved += () => _saves++;
        _actions = new CameraActions(_cameras, id => _tiles.Where(t => t.Camera.Id == id), _leases);
    }

    public void Dispose()
    {
        _leases.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    TileViewModel AddTile(Camera camera, bool manage = true)
    {
        var tile = new TileViewModel(camera, StreamKind.Main, manage, null);
        _tiles.Add(tile);
        return tile;
    }

    Camera Reloaded(Guid id)
    {
        var repo = new CameraRepository(new CameraStore(Path.Combine(_dir, "cameras.json"), new FakeSecretStore()));
        repo.Load();
        return repo.Find(id)!;
    }

    [Fact]
    public void Quality_flips_saves_shows_on_every_tile_and_asks_for_a_rebuild()
    {
        var grid = AddTile(_camera);
        var fullscreen = AddTile(_camera.Clone(), manage: false);
        var other = AddTile(_other);
        var rebuilds = 0;
        _actions.ViewChanged += () => rebuilds++;

        _actions.ToggleQuality(_camera.Id);

        Assert.True(_camera.LowQualityWhenBig);
        Assert.Equal(1, _saves);
        Assert.True(Reloaded(_camera.Id).LowQualityWhenBig);
        Assert.True(grid.LowQuality);
        Assert.True(fullscreen.LowQuality);
        Assert.False(other.LowQuality);
        Assert.Equal(1, rebuilds);

        _actions.ToggleQuality(_camera.Id);
        Assert.False(_camera.LowQualityWhenBig);
        Assert.False(grid.LowQuality);
    }

    [Fact]
    public void Smoothing_flips_saves_and_shows_on_every_tile_without_a_rebuild()
    {
        var grid = AddTile(_camera);
        var fullscreen = AddTile(_camera, manage: false);
        var rebuilds = 0;
        _actions.ViewChanged += () => rebuilds++;

        _actions.ToggleSmoothing(_camera.Id);

        Assert.True(_camera.Smoothing);
        Assert.True(Reloaded(_camera.Id).Smoothing);
        Assert.True(grid.SmoothingOn);
        Assert.True(fullscreen.SmoothingOn);
        Assert.Equal(1, _saves);
        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public void Motion_flips_saves_and_shows_the_indicator_on_every_tile()
    {
        _camera.MotionEnabled = true;
        var grid = AddTile(_camera);
        var fullscreen = AddTile(_camera, manage: false);
        _actions.Show(grid);
        _actions.Show(fullscreen);

        _actions.ToggleMotion(_camera.Id);

        Assert.False(_camera.MotionEnabled);
        Assert.False(Reloaded(_camera.Id).MotionEnabled);
        Assert.False(grid.MotionEnabled);
        Assert.False(fullscreen.MotionEnabled);
        Assert.Equal("Detección de movimiento: desactivada", grid.MotionToolTip);
        Assert.Equal(1, _saves);
    }

    [Fact]
    public void Motion_toggle_asks_to_apply_detection_after_saving()
    {
        var savesSeen = -1;
        _actions.DetectionChanged += () => savesSeen = _saves;

        _actions.ToggleMotion(_camera.Id);

        Assert.Equal(1, savesSeen);
    }

    [Fact]
    public void A_tile_with_an_older_copy_changes_the_stored_camera()
    {
        var stale = _camera.Clone();
        var tile = AddTile(stale);

        _actions.ToggleSmoothing(tile.Camera.Id);

        Assert.True(_camera.Smoothing);
        Assert.False(stale.Smoothing);
        Assert.True(tile.SmoothingOn);
    }

    [Fact]
    public void A_deleted_camera_changes_nothing()
    {
        var tile = AddTile(_camera);
        _cameras.Remove(_camera.Id);
        var rebuilds = 0;
        _actions.ViewChanged += () => rebuilds++;
        var detections = 0;
        _actions.DetectionChanged += () => detections++;

        _actions.ToggleQuality(_camera.Id);
        _actions.ToggleSmoothing(_camera.Id);
        _actions.ToggleMotion(_camera.Id);

        Assert.Equal(0, _saves);
        Assert.Equal(0, rebuilds);
        Assert.Equal(0, detections);
        Assert.False(tile.LowQuality);
        Assert.False(tile.SmoothingOn);
    }

    [Fact]
    public void Show_uses_the_stored_camera()
    {
        _camera.MotionEnabled = true;
        var tile = AddTile(_camera.Clone());
        _camera.LowQualityWhenBig = true;
        _camera.Smoothing = true;
        _camera.MotionEnabled = false;

        _actions.Show(tile);

        Assert.True(tile.LowQuality);
        Assert.True(tile.SmoothingOn);
        Assert.False(tile.MotionEnabled);
    }
}
