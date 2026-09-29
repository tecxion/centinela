namespace Centinela.Core.Playback;

public enum PlayoutAction { Wait, Present, Skip }

/// <param name="WaitSeconds">For <see cref="PlayoutAction.Wait"/>: how long until the head packet is due.</param>
public readonly record struct PlayoutDecision(PlayoutAction Action, double WaitSeconds = 0);

/// <summary>
/// Jitter-buffer clock of a smoothed live view: plays packets at their own timestamps, a target delay behind
/// the newest one received, so bursts from an irregular link come out evenly. All times are in seconds.
/// <list type="bullet">
/// <item>Starts (and restarts after running dry) by buffering the target delay; each dry run adds a step, up to the maximum.</item>
/// <item>A backlog a little over the target is caught up by playing 10 % faster; a large one jumps ahead,
/// and the packets left behind are decoded without being shown (<see cref="PlayoutAction.Skip"/>).</item>
/// </list>
/// Single-threaded: used by the session's playout thread only.
/// </summary>
public sealed class PlayoutClock(double initialDelay, double maxDelay = 2.0)
{
    const double DelayStep = 0.25;
    const double DiscontinuitySeconds = 5;
    const double CatchUpRate = 1.1;
    const double CatchUpMargin = 0.3;
    const double JumpMargin = 1.0;
    const double LateSeconds = 0.25;
    const double StarveGrace = 0.2;
    const double BufferingPoll = 0.05;
    const double Epsilon = 1e-9;

    bool _started;
    double _anchorMedia;
    double _anchorWall;
    double _rate = 1;

    public double TargetDelay { get; private set; } = Math.Min(initialDelay, maxDelay);
    public bool Buffering { get; private set; } = true;

    double Position(double now) => Buffering ? _anchorMedia : _anchorMedia + (now - _anchorWall) * _rate;

    void Anchor(double media, double now, double rate)
    {
        _anchorMedia = media;
        _anchorWall = now;
        _rate = rate;
    }

    /// <summary>What to do now with the oldest queued packet, given the newest timestamp received.</summary>
    public PlayoutDecision Next(double headPts, double newestPts, double now)
    {
        if (!_started)
        {
            _started = true;
            Buffering = true;
            _anchorMedia = headPts;
        }
        else if (Math.Abs(headPts - Position(now)) > DiscontinuitySeconds)
        {
            Buffering = true;
            _anchorMedia = headPts;
        }

        if (Buffering)
        {
            if (newestPts - _anchorMedia < TargetDelay - Epsilon) return new(PlayoutAction.Wait, BufferingPoll);
            Buffering = false;
            Anchor(_anchorMedia, now, 1);
        }

        var position = Position(now);
        var depth = newestPts - position;
        if (depth > TargetDelay + JumpMargin)
        {
            Anchor(newestPts - TargetDelay, now, 1);
            position = _anchorMedia;
            depth = TargetDelay;
        }
        var rate = depth > TargetDelay + CatchUpMargin ? CatchUpRate : 1.0;
        if (rate != _rate) Anchor(position, now, rate);

        if (headPts <= position + Epsilon)
            return new(headPts < position - LateSeconds ? PlayoutAction.Skip : PlayoutAction.Present);
        return new(PlayoutAction.Wait, (headPts - position) / _rate);
    }

    /// <summary>The queue is empty: once playback has run past the newest packet, it stops and rebuffers.</summary>
    public void Idle(double newestPts, double now)
    {
        if (!_started || Buffering) return;
        if (Position(now) <= newestPts + StarveGrace) return;
        _anchorMedia = newestPts;
        Starve();
    }

    /// <summary>Ran dry: buffer again, one step longer (up to the maximum).</summary>
    public void Starve()
    {
        Buffering = true;
        TargetDelay = Math.Min(TargetDelay + DelayStep, maxDelay);
    }
}
