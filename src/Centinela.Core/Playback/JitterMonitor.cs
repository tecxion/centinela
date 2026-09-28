namespace Centinela.Core.Playback;

/// <summary>
/// Detects a link that delivers a live stream in stalls and bursts (weak Wi-Fi): a stall is a video packet that
/// arrives more than <c>stallSeconds</c> later after the previous one than their timestamps are apart. The link
/// is unstable once <c>stallCount</c> stalls fall within <c>windowSeconds</c>, so a single hiccup (or a one-off
/// timestamp rebase when the camera's clock sync arrives) is not enough.
/// </summary>
public sealed class JitterMonitor(double stallSeconds = 0.3, int stallCount = 2, double windowSeconds = 30)
{
    // Timestamps that move more than this between two packets are a discontinuity, not a stall.
    const double DiscontinuitySeconds = 10;

    readonly Queue<(double Arrival, double Lateness)> _stalls = new();
    double _previousArrival = double.NaN;
    double _previousPts = double.NaN;

    /// <summary>Longest stall in the window, in seconds (0 = none).</summary>
    public double WorstStall => _stalls.Count == 0 ? 0 : _stalls.Max(s => s.Lateness);

    /// <summary>Adds a video packet (both in seconds, in arrival order); true when the link is unstable.</summary>
    public bool Add(double arrival, double pts)
    {
        if (!double.IsNaN(_previousPts) && Math.Abs(pts - _previousPts) <= DiscontinuitySeconds)
        {
            var lateness = arrival - _previousArrival - (pts - _previousPts);
            if (lateness > stallSeconds) _stalls.Enqueue((arrival, lateness));
        }
        _previousArrival = arrival;
        _previousPts = pts;
        while (_stalls.Count > 0 && _stalls.Peek().Arrival < arrival - windowSeconds) _stalls.Dequeue();
        return _stalls.Count >= stallCount;
    }

    public void Reset()
    {
        _stalls.Clear();
        _previousArrival = double.NaN;
        _previousPts = double.NaN;
    }
}
