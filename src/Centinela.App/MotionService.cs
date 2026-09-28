using System.Windows.Threading;
using Centinela.Core;
using Centinela.Media;

namespace Centinela.App;

/// <summary>
/// Always-on motion detection for the cameras that have it enabled, on a lease of their shared substream session.
/// A background timer (200 ms) reads each session's newest frame, reducing it to 160×90 gray inside the mailbox
/// lock (the only work done there) and analysing it outside. Transitions reach the UI thread in order, through a
/// per-monitor queue the UI drains; stopping a monitor drains it synchronously, so no event is lost or late.
/// Public members are for the UI thread.
/// </summary>
public sealed class MotionService : IDisposable
{
    static readonly TimeSpan Period = TimeSpan.FromMilliseconds(200);
    static readonly MotionSample NoMotion = new(false, 0);

    readonly Func<Camera, SharedStreamLease?> _acquire;
    readonly Dispatcher _dispatcher;
    readonly Func<MotionSensitivity, IMotionDetector> _detectorFactory;
    readonly Dictionary<Guid, CameraMonitor> _monitors = [];   // UI thread only
    // Releases of stopped monitors that may still be running; ShutdownAsync hands them to the caller (UI thread).
    readonly List<Task> _releases = [];
    readonly Timer _timer;
    // What the timer iterates: replaced as a whole on the UI thread, read by ticks.
    volatile CameraMonitor[] _running = [];
    int _ticking;
    bool _closing;   // UI thread
    Task? _shutdown;

    public MotionService(Func<Camera, SharedStreamLease?> acquire, Dispatcher dispatcher,
        Func<MotionSensitivity, IMotionDetector>? detectorFactory = null)
    {
        _acquire = acquire;
        _dispatcher = dispatcher;
        _detectorFactory = detectorFactory ?? (s => new FrameDiffDetector(s));
        _timer = new Timer(_ => Tick(), null, Period, Period);
    }

    /// <summary>Raised on the UI thread when a camera's motion event starts (true) or ends (false).</summary>
    public event Action<Guid, bool>? MotionChanged;
    /// <summary>Raised on the UI thread when an event starts; alert = the camera's cooldown has passed.</summary>
    public event Action<Camera, bool>? MotionStarted;
    /// <summary>Raised on the UI thread when an event ends (also when detection stops with an event active).</summary>
    public event Action<Camera, MotionEvent>? MotionEnded;
    /// <summary>Raised on the UI thread when the detector failed; that camera's detection stays off until its options change.</summary>
    public event Action<Camera, string>? DetectorFailed;

    /// <summary>
    /// Monitors exactly the cameras with <see cref="Camera.MotionEnabled"/>: starts new ones, restarts those whose
    /// substream or sensitivity changed, updates the rest in place and stops (flushing their events) the others.
    /// </summary>
    public void Apply(IReadOnlyList<Camera> cameras)
    {
        if (_closing) return;
        // Every change is made before any event is raised, so handlers may call back into this service.
        var stopped = new List<CameraMonitor>();
        var wanted = new HashSet<Guid>();
        foreach (var camera in cameras)
        {
            if (!camera.MotionEnabled || KeyOf(camera) is not { } key) continue;
            wanted.Add(camera.Id);
            if (_monitors.TryGetValue(camera.Id, out var monitor))
            {
                if (monitor.Key == key)
                {
                    monitor.Camera = camera;
                    lock (monitor.Gate) monitor.Tracker.Cooldown = CooldownOf(camera);
                    continue;
                }
                _monitors.Remove(camera.Id);
                stopped.Add(StopQuietly(monitor));
            }
            if (_acquire(camera) is not { } lease)
            {
                wanted.Remove(camera.Id);
                continue;
            }
            _monitors[camera.Id] = new CameraMonitor(camera, key, lease, _detectorFactory(camera.MotionSensitivity));
        }
        foreach (var id in _monitors.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            _monitors.Remove(id, out var monitor);
            stopped.Add(StopQuietly(monitor!));
        }
        _running = [.. _monitors.Values];
        foreach (var monitor in stopped) Drain(monitor);
    }

    /// <summary>True while the camera has a motion event in progress (as last raised on the UI thread).</summary>
    public bool IsActive(Guid id) => _monitors.TryGetValue(id, out var monitor) && monitor.Active;

    /// <summary>
    /// Stops detection: ends active events (raising <see cref="MotionEnded"/> synchronously, on this UI thread) and
    /// releases every lease. The task completes when the sessions this service held last are disposed; repeated
    /// calls return the same task.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        // Handlers of the events raised below may call Apply: it does nothing from now on.
        _closing = true;
        _timer.Dispose();
        _running = [];
        var monitors = _monitors.Values.Select(StopQuietly).ToList();
        _monitors.Clear();
        lock (_releases)
        {
            _shutdown = Task.WhenAll(_releases);
            _releases.Clear();
        }
        foreach (var monitor in monitors) Drain(monitor);
        return _shutdown;
    }

    public void Dispose() => _ = ShutdownAsync();

    /// <summary>The settings that need a new session or detector when they change; null = no usable substream.</summary>
    static MonitorKey? KeyOf(Camera camera)
    {
        try
        {
            return new MonitorKey(StreamUrlBuilder.Build(camera, StreamKind.Sub), camera.UseUdp, camera.MotionSensitivity);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    static TimeSpan CooldownOf(Camera camera) => TimeSpan.FromSeconds(Camera.NormalizeCooldown(camera.MotionCooldownSeconds));

    /// <summary>
    /// UI thread: no tick touches the monitor after this; its active event is ended and queued, and the caller
    /// raises it with <see cref="Drain"/> once its own changes are done.
    /// </summary>
    CameraMonitor StopQuietly(CameraMonitor monitor)
    {
        lock (monitor.Gate)
        {
            if (!monitor.Stopped)
            {
                monitor.Stopped = true;
                monitor.Enqueue(monitor.Tracker.Flush(DateTimeOffset.Now));
            }
            Release(monitor);
        }
        return monitor;
    }

    // Caller holds the monitor's gate. Idempotent.
    void Release(CameraMonitor monitor)
    {
        if (monitor.Lease is not { } lease) return;
        monitor.Lease = null;
        var release = lease.ReleaseAsync();
        if (release.IsCompleted) return;
        lock (_releases)
        {
            _releases.RemoveAll(t => t.IsCompleted);
            _releases.Add(release);
        }
    }

    void Tick()
    {
        // Skip a tick while the previous one is still running.
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0) return;
        try
        {
            foreach (var monitor in _running)
                if (Process(monitor))
                    _dispatcher.BeginInvoke(() => Drain(monitor));
        }
        catch (Exception)
        {
            // Never let the timer thread bring the app down (the dispatcher may be shutting down).
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    /// <summary>Timer thread. True when it queued something for the UI.</summary>
    bool Process(CameraMonitor monitor)
    {
        lock (monitor.Gate)
        {
            if (monitor.Stopped || monitor.Lease is not { } lease) return false;
            var now = DateTimeOffset.Now;
            try
            {
                var session = lease.Session;
                if (session.State != SessionState.Playing)
                {
                    // No image, no motion: an active event ends on time. Start afresh when it plays again.
                    monitor.NeedsReset = true;
                    return monitor.Enqueue(monitor.Tracker.Update(NoMotion, now));
                }
                if (monitor.NeedsReset)
                {
                    monitor.Detector.Reset();
                    monitor.NeedsReset = false;
                }
                var gray = monitor.Gray;
                // Only the downscale runs inside the mailbox lock; analysis runs after it.
                var read = session.Mailbox.TryRead(ref monitor.Sequence,
                    f => GrayScaler.FromBgra(f.Data, f.Width, f.Height, f.Stride, gray));
                var sample = read ? monitor.Detector.Analyze(gray) : NoMotion;
                return monitor.Enqueue(monitor.Tracker.Update(sample, now));
            }
            catch (Exception ex)
            {
                // Detection of this camera stays off until its options change; its video is unaffected.
                monitor.Stopped = true;
                monitor.Enqueue(monitor.Tracker.Flush(now));
                monitor.Failure = CredentialSanitizer.Sanitize(ex.Message);
                Release(monitor);
                return true;
            }
        }
    }

    /// <summary>UI thread: raises the monitor's queued transitions in order.</summary>
    void Drain(CameraMonitor monitor)
    {
        while (true)
        {
            MotionUpdate update;
            string? failure = null;
            lock (monitor.Gate)
            {
                if (monitor.Pending.Count > 0) update = monitor.Pending.Dequeue();
                else
                {
                    failure = monitor.Failure;
                    monitor.Failure = null;
                    update = MotionUpdate.None;
                }
            }
            var camera = monitor.Camera;
            switch (update.Transition)
            {
                case MotionTransition.Started:
                    monitor.Active = true;
                    MotionChanged?.Invoke(camera.Id, true);
                    MotionStarted?.Invoke(camera, update.Alert);
                    continue;
                case MotionTransition.Ended:
                    monitor.Active = false;
                    MotionChanged?.Invoke(camera.Id, false);
                    if (update.Event is { } motionEvent) MotionEnded?.Invoke(camera, motionEvent);
                    continue;
            }
            if (failure is not null) DetectorFailed?.Invoke(camera, failure);
            return;
        }
    }

    sealed record MonitorKey(string Url, bool UseUdp, MotionSensitivity Sensitivity);

    sealed class CameraMonitor(Camera camera, MonitorKey key, SharedStreamLease lease, IMotionDetector detector)
    {
        public readonly object Gate = new();
        public readonly MonitorKey Key = key;
        public readonly IMotionDetector Detector = detector;
        public readonly MotionTracker Tracker = new(CooldownOf(camera));
        public readonly GrayFrame Gray = GrayScaler.Create();
        // Guarded by Gate.
        public readonly Queue<MotionUpdate> Pending = new();
        public SharedStreamLease? Lease = lease;
        public bool Stopped;
        public bool NeedsReset;
        public long Sequence;
        public string? Failure;
        // UI thread only.
        public Camera Camera = camera;
        public bool Active;

        /// <summary>Caller holds Gate. True when the update was queued for the UI.</summary>
        public bool Enqueue(MotionUpdate update)
        {
            if (update.Transition == MotionTransition.None) return false;
            Pending.Enqueue(update);
            return true;
        }
    }
}
