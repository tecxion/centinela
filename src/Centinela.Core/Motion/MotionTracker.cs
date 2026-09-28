namespace Centinela.Core;

public enum MotionTransition { None, Started, Ended }

public sealed record MotionEvent(DateTimeOffset Start, DateTimeOffset End, double Peak);

public sealed record MotionUpdate(MotionTransition Transition, bool Alert, MotionEvent? Event)
{
    public static readonly MotionUpdate None = new(MotionTransition.None, false, null);
}

/// <summary>
/// Turns per-frame samples of one camera into events: starts on the first motion sample, ends after
/// <see cref="EndAfter"/> without motion. Alerts only on start, at most once per <see cref="Cooldown"/>.
/// </summary>
public sealed class MotionTracker(TimeSpan cooldown)
{
    public static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(3);

    DateTimeOffset _start, _lastMotion;
    DateTimeOffset? _lastAlert;
    double _peak;

    public TimeSpan Cooldown { get; set; } = cooldown;
    public bool IsActive { get; private set; }

    public MotionUpdate Update(MotionSample sample, DateTimeOffset now)
    {
        if (sample.Motion)
        {
            if (IsActive)
            {
                _lastMotion = now;
                _peak = Math.Max(_peak, sample.ChangedFraction);
                return MotionUpdate.None;
            }
            IsActive = true;
            _start = _lastMotion = now;
            _peak = sample.ChangedFraction;
            var alert = _lastAlert is not { } last || now - last >= Cooldown;
            if (alert) _lastAlert = now;
            return new MotionUpdate(MotionTransition.Started, alert, null);
        }
        return IsActive && now - _lastMotion >= EndAfter ? End() : MotionUpdate.None;
    }

    /// <summary>
    /// Ends an active event immediately (detection turned off, camera removed, app closing). The event ends at
    /// the last motion seen; <paramref name="now"/> is kept for callers' clarity and is not used.
    /// </summary>
    public MotionUpdate Flush(DateTimeOffset now) => IsActive ? End() : MotionUpdate.None;

    MotionUpdate End()
    {
        IsActive = false;
        return new MotionUpdate(MotionTransition.Ended, false, new MotionEvent(_start, _lastMotion, _peak));
    }
}
