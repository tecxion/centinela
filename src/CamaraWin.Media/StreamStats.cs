namespace CamaraWin.Media;

/// <summary>Live-view health: frames per second, packet-received→frame-published latency, decoder, freshness.</summary>
public sealed record StreamStats(double Fps, double LatencyMs, bool HardwareDecoding, TimeSpan SinceLastFrame)
{
    public static StreamStats Empty { get; } = new(0, 0, false, TimeSpan.Zero);
}
