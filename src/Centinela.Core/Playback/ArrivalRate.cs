namespace Centinela.Core.Playback;

/// <summary>
/// How much video a link delivers per second of wall time, over the last <c>windowSeconds</c>: 1.0 = keeps up
/// with the camera; below 1 the link cannot carry the stream and no buffer can hide it.
/// </summary>
public sealed class ArrivalRate(double windowSeconds = 10)
{
    const double DiscontinuitySeconds = 10;
    readonly Queue<(double Arrival, double Pts)> _samples = new();
    double _lastPts;

    /// <summary>Media seconds received per wall second; NaN until the window spans a couple of seconds.</summary>
    public double Ratio { get; private set; } = double.NaN;

    public void Add(double arrival, double pts)
    {
        if (_samples.Count > 0 && Math.Abs(pts - _lastPts) > DiscontinuitySeconds) _samples.Clear();
        _lastPts = pts;
        _samples.Enqueue((arrival, pts));
        while (_samples.Peek().Arrival < arrival - windowSeconds) _samples.Dequeue();
        var first = _samples.Peek();
        var wall = arrival - first.Arrival;
        Ratio = wall < 2 ? double.NaN : (pts - first.Pts) / wall;
    }

    public void Reset()
    {
        _samples.Clear();
        Ratio = double.NaN;
    }
}
