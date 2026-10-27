using Centinela.Core;
using Centinela.Desktop.Services.Motion;
using Centinela.Media;

namespace Centinela.Desktop.Tests;

/// <summary>
/// Which cameras motion detection watches. The acquire hands out no lease (no session, no network), so it records
/// which cameras the service tried to watch; the detector factory is a fake that counts its uses.
/// </summary>
public sealed class MotionServiceTests : IDisposable
{
    readonly List<Camera> _acquired = [];
    int _detectors;
    readonly MotionService _motion;

    public MotionServiceTests()
    {
        _motion = new MotionService(camera =>
        {
            _acquired.Add(camera);
            return (SharedStreamLease?)null;
        }, _ =>
        {
            _detectors++;
            return new FakeDetector();
        });
    }

    public void Dispose() => _motion.Dispose();

    static Camera Tapo(string name, bool motion) => new()
    {
        Name = name, Brand = Brand.Tapo, Host = "192.168.1.20", Port = 554, User = "u", Password = "p", MotionEnabled = motion,
    };

    [Fact]
    public void Apply_watches_only_cameras_with_detection_on_and_a_substream()
    {
        var on = Tapo("Patio", motion: true);
        var off = Tapo("Garaje", motion: false);
        var noUrl = new Camera { Name = "Sin URL", Brand = Brand.Custom, MotionEnabled = true };

        _motion.Apply([on, off, noUrl]);

        Assert.Equal([on], _acquired);
        // No lease: nothing is watched, so no detector is made.
        Assert.Equal(0, _detectors);
    }

    [Fact]
    public void Apply_without_the_camera_stops_watching_it()
    {
        var camera = Tapo("Patio", motion: true);
        _motion.Apply([camera]);
        _acquired.Clear();

        _motion.Apply([]);

        Assert.Empty(_acquired);
        Assert.False(_motion.IsActive(camera.Id));
    }

    [Fact]
    public void Turning_detection_off_stops_watching_it()
    {
        var camera = Tapo("Patio", motion: true);
        _motion.Apply([camera]);
        _acquired.Clear();
        camera.MotionEnabled = false;

        _motion.Apply([camera]);

        Assert.Empty(_acquired);
        Assert.False(_motion.IsActive(camera.Id));
    }

    [Fact]
    public void No_camera_is_active_without_events()
    {
        var camera = Tapo("Patio", motion: true);
        _motion.Apply([camera]);

        Assert.False(_motion.IsActive(camera.Id));
        Assert.False(_motion.IsActive(Guid.NewGuid()));
    }

    [Fact]
    public void Confirming_an_alert_for_an_unknown_camera_does_nothing() =>
        _motion.ConfirmAlert(Guid.NewGuid(), DateTimeOffset.Now);

    [Fact]
    public async Task Shutdown_completes_and_then_Apply_does_nothing()
    {
        var camera = Tapo("Patio", motion: true);
        _motion.Apply([camera]);
        _acquired.Clear();

        var shutdown = _motion.ShutdownAsync();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        _motion.Apply([camera]);

        Assert.Empty(_acquired);
        Assert.Same(shutdown, _motion.ShutdownAsync());
    }

    sealed class FakeDetector : IMotionDetector
    {
        public MotionSample Analyze(GrayFrame frame) => new(false, 0);
        public void Reset() { }
    }
}
