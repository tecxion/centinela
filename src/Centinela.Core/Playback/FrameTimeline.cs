namespace Centinela.Core.Playback;

/// <summary>
/// A steady timeline for a live stream built from arrivals, not from the camera's timestamps (some cameras'
/// timestamps jump whenever their RTCP clock sync arrives). Each new frame advances the timeline by the
/// average frame interval measured over the last <c>windowSeconds</c> of arrivals, so frame n sits at about
/// n × interval however irregularly it arrived. All times are in seconds.
/// </summary>
public sealed class FrameTimeline(double windowSeconds = 10, double defaultInterval = 0.04)
{
    const double MinInterval = 1.0 / 60;
    const double MaxInterval = 0.5;
    const double MinMeasuredSeconds = 2;

    readonly Queue<double> _frameStarts = new();
    double _key = double.NaN;

    /// <summary>Average time between frames (the camera's real frame rate as delivered).</summary>
    public double Interval { get; private set; } = defaultInterval;

    /// <summary>
    /// Timeline position of a video packet. <paramref name="newFrame"/> is false for further packets of the
    /// frame already started (same camera timestamp), which share its position.
    /// </summary>
    public double Add(double arrival, bool newFrame)
    {
        if (double.IsNaN(_key))
        {
            _key = 0;
            _frameStarts.Enqueue(arrival);
            return _key;
        }
        if (!newFrame) return _key;

        _frameStarts.Enqueue(arrival);
        while (_frameStarts.Peek() < arrival - windowSeconds) _frameStarts.Dequeue();
        var span = arrival - _frameStarts.Peek();
        if (span >= MinMeasuredSeconds && _frameStarts.Count >= 3)
            Interval = Math.Clamp(span / (_frameStarts.Count - 1), MinInterval, MaxInterval);
        _key += Interval;
        return _key;
    }

    public void Reset()
    {
        _frameStarts.Clear();
        _key = double.NaN;
        Interval = defaultInterval;
    }
}
