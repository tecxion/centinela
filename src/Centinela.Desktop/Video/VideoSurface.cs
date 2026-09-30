using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Centinela.Media;

namespace Centinela.Desktop.Video;

/// <summary>
/// Paints the newest frame of a session's mailbox on every screen refresh: latest wins, nothing is queued, so no
/// latency accumulates (same rule as the Windows version). Letterboxed, centred.
/// </summary>
sealed class VideoSurface : Control
{
    WriteableBitmap? _bitmap;
    long _sequence;
    bool _attached;
    bool _firstFrame;
    // One animation-frame request at a time; a request made before the last detach is stale (its generation changed).
    bool _frameRequested;
    int _generation;

    StreamSession? _session;

    /// <summary>The session to paint; a new one starts reading its mailbox from the beginning.</summary>
    public StreamSession? Session
    {
        get => _session;
        set
        {
            if (value == _session) return;
            _session = value;
            _sequence = 0;
            RequestFrame();
        }
    }

    /// <summary>Raised once, after the first frame has been painted.</summary>
    public event Action? FirstFrameShown;

    /// <summary>Where the image sits inside the control (letterboxed); empty until the first frame.</summary>
    public Rect ContentRect { get; private set; }

    /// <summary>Raised when <see cref="ContentRect"/> changes (control resized or new frame size).</summary>
    public event Action? ContentRectChanged;

    public VideoSurface() => SizeChanged += (_, _) => UpdateContentRect();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameRequested = false;
        _generation++;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Asks for the next screen refresh; not while detached, without a session or with a request pending.</summary>
    void RequestFrame()
    {
        if (!_attached || _session is null || _frameRequested) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _frameRequested = true;
        var generation = _generation;
        topLevel.RequestAnimationFrame(_ => OnFrame(generation));
    }

    void OnFrame(int generation)
    {
        // Requested before a detach: the current placement (if any) has its own request.
        if (generation != _generation) return;
        _frameRequested = false;
        if (!_attached) return;
        if (_session is { } session && session.Mailbox.TryRead(ref _sequence, CopyFrame))
        {
            UpdateContentRect();
            InvalidateVisual();
            if (!_firstFrame)
            {
                _firstFrame = true;
                FirstFrameShown?.Invoke();
            }
        }
        RequestFrame();
    }

    void CopyFrame(VideoFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using var target = _bitmap.Lock();
        if (target.RowBytes == frame.Stride)
        {
            Marshal.Copy(frame.Data, 0, target.Address, frame.Stride * frame.Height);
            return;
        }
        for (var row = 0; row < frame.Height; row++)
            Marshal.Copy(frame.Data, row * frame.Stride, target.Address + row * target.RowBytes, frame.Stride);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is not { } bitmap) return;
        var source = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        context.DrawImage(bitmap, source, Letterbox(source.Size, Bounds.Size));
    }

    void UpdateContentRect()
    {
        var rect = _bitmap is { } bitmap
            ? Letterbox(new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds.Size)
            : default;
        if (rect == ContentRect) return;
        ContentRect = rect;
        ContentRectChanged?.Invoke();
    }

    /// <summary>The largest rectangle with the image's aspect ratio that fits the control, centred.</summary>
    static Rect Letterbox(Size image, Size bounds)
    {
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var size = new Size(image.Width * scale, image.Height * scale);
        return new Rect(new Point((bounds.Width - size.Width) / 2, (bounds.Height - size.Height) / 2), size);
    }
}
