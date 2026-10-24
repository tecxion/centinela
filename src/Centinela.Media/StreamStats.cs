namespace Centinela.Media;

/// <summary>
/// Live-view health: frames per second, packet-received→frame-published latency, decoder, freshness, for a
/// smoothed view its delay in seconds (0 = frames shown as they arrive) and how often it ran dry, and how long the
/// processor takes to convert and scale each frame (milliseconds, averaged), and whether its frames are NV12 for the
/// graphics card to paint.
/// </summary>
public sealed record StreamStats(double Fps, double LatencyMs, bool HardwareDecoding, TimeSpan SinceLastFrame,
    double SmoothingSeconds = 0, int Rebuffers = 0, double ConvertMs = 0, bool GpuPainting = false)
{
    public static StreamStats Empty { get; } = new(0, 0, false, TimeSpan.Zero);
}
