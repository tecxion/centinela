namespace Centinela.Media;

/// <summary>
/// Live-view health: frames per second, packet-received→frame-published latency, decoder, freshness and the
/// smoothing delay in seconds (0 = frames shown as they arrive).
/// </summary>
public sealed record StreamStats(double Fps, double LatencyMs, bool HardwareDecoding, TimeSpan SinceLastFrame, double SmoothingSeconds = 0)
{
    public static StreamStats Empty { get; } = new(0, 0, false, TimeSpan.Zero);
}
