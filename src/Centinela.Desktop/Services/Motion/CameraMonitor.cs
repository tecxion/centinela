using Centinela.Core;
using Centinela.Media;

namespace Centinela.Desktop.Services.Motion;

/// <summary>The settings that need a new session when they change.</summary>
sealed record MonitorKey(string Url, bool UseUdp);

/// <summary>
/// Detection state of one camera: its lease, detector and tracker (guarded by <see cref="Gate"/>, shared by the
/// timer and the UI thread) and the transitions queued for the UI thread, raised in order by the service.
/// </summary>
sealed class CameraMonitor(Camera camera, MonitorKey key, SharedStreamLease lease, MotionSensitivity sensitivity,
    IMotionDetector detector)
{
    public readonly object Gate = new();
    public readonly MonitorKey Key = key;
    public readonly MotionTracker Tracker = new(CooldownOf(camera));
    public readonly GrayFrame Gray = GrayScaler.Create();
    // Guarded by Gate.
    public readonly Queue<MotionUpdate> Pending = new();
    public MotionSensitivity Sensitivity = sensitivity;
    public IMotionDetector Detector = detector;
    public SharedStreamLease? Lease = lease;
    public bool Stopped;
    public bool NeedsReset;
    public long Sequence;
    public string? Failure;
    // UI thread only.
    public Camera Camera = camera;
    public bool Active;

    public static TimeSpan CooldownOf(Camera camera) =>
        TimeSpan.FromSeconds(Camera.NormalizeCooldown(camera.MotionCooldownSeconds));

    /// <summary>Caller holds Gate. True when the update was queued for the UI.</summary>
    public bool Enqueue(MotionUpdate update)
    {
        if (update.Transition == MotionTransition.None) return false;
        Pending.Enqueue(update);
        return true;
    }
}
