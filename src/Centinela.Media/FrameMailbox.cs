namespace Centinela.Media;

/// <summary>A BGRA image. Stride is always Width * 4.</summary>
public sealed class VideoFrame(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = width * 4;
    public byte[] Data { get; } = new byte[width * 4 * height];
}

/// <summary>
/// Single-slot, latest-wins handoff between the decode thread and the UI.
/// Two buffers rotate: the producer fills the one it rented while the consumer reads the latest.
/// Stale frames are overwritten, never queued, so display latency never accumulates.
/// </summary>
public sealed class FrameMailbox
{
    readonly object _gate = new();
    VideoFrame? _latest;
    VideoFrame? _spare;
    long _sequence;

    public long Sequence => Interlocked.Read(ref _sequence);

    public VideoFrame Rent(int width, int height)
    {
        lock (_gate)
        {
            var frame = _spare;
            _spare = null;
            return frame is not null && frame.Width == width && frame.Height == height
                ? frame
                : new VideoFrame(width, height);
        }
    }

    public void Publish(VideoFrame frame)
    {
        lock (_gate)
        {
            _spare = _latest;
            _latest = frame;
            Interlocked.Increment(ref _sequence);
        }
    }

    public bool TryRead(ref long lastSequence, Action<VideoFrame> read)
    {
        lock (_gate)
        {
            if (_latest is null || _sequence == lastSequence) return false;
            read(_latest);
            lastSequence = _sequence;
            return true;
        }
    }
}
