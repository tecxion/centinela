using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Centinela.Desktop.Services.Ui;
using Centinela.Media;

namespace Centinela.Desktop.Video;

/// <summary>
/// Paints the newest frame of a session's mailbox on every screen refresh: latest wins, nothing is queued, so no
/// latency accumulates (same rule as the Windows version). Letterboxed, centred. While OpenGL works its
/// <see cref="GlVideoView"/> child paints (and zooms); otherwise BGRA frames are copied into a bitmap
/// (<see cref="GpuVideo"/>).
/// </summary>
sealed class VideoSurface : Control
{
    // A view that has frames to show but paints none for this long (its OpenGL never starts, or starts but Avalonia
    // never runs its render) sends everybody back to the processor.
    static readonly TimeSpan PaintTimeout = TimeSpan.FromSeconds(3);

    // Updated in place: panning must not allocate on every pointer move.
    readonly MatrixTransform _transform = new();
    WriteableBitmap? _bitmap;
    GlVideoView? _gl;
    PixelSize _frameSize;
    Matrix? _zoom;
    long _unpaintedSince; // Stopwatch ticks since this view has had a frame it has not painted; 0 = none
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

    /// <summary>Where the image sits inside the control (letterboxed, before the zoom); empty until the first frame.</summary>
    public Rect ContentRect { get; private set; }

    /// <summary>Raised when <see cref="ContentRect"/> changes (control resized or new frame size).</summary>
    public event Action? ContentRectChanged;

    public VideoSurface()
    {
        // The zoom matrix is built for the top-left corner; Avalonia centres render transforms by default.
        RenderTransformOrigin = RelativePoint.TopLeft;
        SizeChanged += (_, _) => UpdateContentRect();
        if (GpuVideo.State != GpuVideo.Mode.Off) AddGl();
    }

    /// <summary>Where OpenGL draws the image: <see cref="ContentRect"/> moved by the zoom.</summary>
    internal Rect DrawRect => _zoom is { } zoom ? ContentRect.TransformToAABB(zoom) : ContentRect;

    /// <summary>The digital zoom in surface coordinates (top-left origin), or null for the whole image.</summary>
    public void SetZoom(Matrix? zoom)
    {
        _zoom = zoom;
        ApplyZoom();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        GpuVideo.TurnedOff += RemoveGl;
        if (GpuVideo.State == GpuVideo.Mode.Off) RemoveGl();
        ApplyZoom();
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameRequested = false;
        _unpaintedSince = 0;
        _generation++;
        GpuVideo.TurnedOff -= RemoveGl;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Called by the OpenGL child once it can paint: the zoom moves into its drawing.</summary>
    internal void OnGlReady()
    {
        _unpaintedSince = 0;
        ApplyZoom();
    }

    /// <summary>For <see cref="GlVideoView"/>: hands it the newest frame not yet shown; false when there is none.</summary>
    internal bool TryReadFrame(Action<VideoFrame> upload)
    {
        if (_session is not { } session) return false;
        var size = default(PixelSize);
        if (!session.Mailbox.TryRead(ref _sequence, frame =>
            {
                size = new PixelSize(frame.Width, frame.Height);
                upload(frame);
            }))
            return false;
        _unpaintedSince = 0;
        if (_bitmap is not null)
        {
            // OpenGL paints now: the processor's last image must not linger underneath.
            _bitmap.Dispose();
            _bitmap = null;
            InvalidateVisual();
        }
        Shown(size, inGlRender: true);
        return true;
    }

    void AddGl()
    {
        _gl = new GlVideoView(this);
        VisualChildren.Add(_gl);
        LogicalChildren.Add(_gl);
    }

    void RemoveGl()
    {
        if (_gl is not { } gl) return;
        _gl = null;
        VisualChildren.Remove(gl);
        LogicalChildren.Remove(gl);
        ApplyZoom();
    }

    /// <summary>
    /// A painting OpenGL child draws the zoom itself, at the decoded resolution; a render transform would enlarge its
    /// already-drawn pixels. The bitmap is zoomed with the render transform, as before.
    /// </summary>
    void ApplyZoom()
    {
        if (_zoom is { } zoom && _gl is not { Ready: true })
        {
            _transform.Matrix = zoom;
            if (!ReferenceEquals(RenderTransform, _transform)) RenderTransform = _transform;
        }
        else
        {
            RenderTransform = null;
        }
        _gl?.RequestNextFrameRendering();
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
        if (_session is { } session)
        {
            if (_gl is { Ready: true } gl)
            {
                if (session.Mailbox.Sequence != _sequence)
                {
                    gl.RequestNextFrameRendering();
                    WatchUnpainted();
                }
            }
            else
            {
                PaintOnProcessor(session);
            }
        }
        RequestFrame();
    }

    void PaintOnProcessor(StreamSession session)
    {
        var size = default(PixelSize);
        var needsGl = false;
        var before = _sequence;
        if (!session.Mailbox.TryRead(ref _sequence, frame =>
            {
                if (frame.Layout != FrameLayout.Bgra)
                {
                    needsGl = true;
                    return;
                }
                CopyFrame(frame);
                size = new PixelSize(frame.Width, frame.Height);
            }))
            return;
        if (needsGl)
        {
            // NV12 needs this view's OpenGL, which is still starting: left unread, its first render takes this frame.
            _sequence = before;
            WatchUnpainted();
            return;
        }
        _unpaintedSince = 0;
        InvalidateVisual();
        Shown(size, inGlRender: false);
    }

    /// <summary>A frame waits to be painted: after <see cref="PaintTimeout"/> of that, everybody uses the processor.</summary>
    void WatchUnpainted()
    {
        if (_unpaintedSince == 0) _unpaintedSince = Stopwatch.GetTimestamp();
        // Without a size OpenGL renders nothing on purpose.
        else if (Stopwatch.GetElapsedTime(_unpaintedSince) > PaintTimeout && Bounds.Width > 0 && Bounds.Height > 0)
            GpuVideo.TurnOff();
    }

    void Shown(PixelSize size, bool inGlRender)
    {
        _frameSize = size;
        UpdateContentRect(inGlRender);
        if (_firstFrame) return;
        _firstFrame = true;
        // Posted: on the OpenGL path this runs inside Avalonia's GL callback, and listeners remove tiles (and their GL).
        UiThread.Post(() => FirstFrameShown?.Invoke());
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

    void UpdateContentRect(bool inGlRender = false)
    {
        var rect = _frameSize.Width > 0
            ? Letterbox(new Size(_frameSize.Width, _frameSize.Height), Bounds.Size)
            : default;
        if (rect == ContentRect) return;
        ContentRect = rect;
        // In Avalonia's GL callback: this draw needs the rect now, but a throwing handler would turn OpenGL off for all.
        if (inGlRender) UiThread.Post(() => ContentRectChanged?.Invoke());
        else ContentRectChanged?.Invoke();
    }

    /// <summary>The largest rectangle with the image's aspect ratio that fits the control, centred.</summary>
    static Rect Letterbox(Size image, Size bounds)
    {
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var size = new Size(image.Width * scale, image.Height * scale);
        return new Rect(new Point((bounds.Width - size.Width) / 2, (bounds.Height - size.Height) / 2), size);
    }
}
