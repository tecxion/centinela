using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Centinela.Core;

namespace Centinela.Desktop.Video;

/// <summary>
/// Digital zoom of a <see cref="VideoSurface"/> inside its host (the element that clips it): wheel zoom towards the
/// pointer and drag to pan, as the Windows version. Points are in host coordinates; the owner decides when zoom is
/// allowed and shows <see cref="Changed"/> (label, menu item, decode size). UI thread only.
/// </summary>
sealed class ZoomPanBehavior
{
    readonly VideoSurface _video;
    readonly Control _host;
    readonly ZoomState _zoom = new();
    // One transform per tile, updated in place: panning must not allocate on every pointer move.
    readonly MatrixTransform _transform = new();
    Point? _panLast;

    public ZoomPanBehavior(VideoSurface video, Control host)
    {
        _video = video;
        _host = host;
        // Avalonia centres render transforms by default; the zoom matrix is built for the top-left corner.
        _video.RenderTransformOrigin = RelativePoint.TopLeft;
        // Re-clamps to the new size or letterbox and updates the decode box.
        _video.ContentRectChanged += ApplyIfZoomed;
        _host.SizeChanged += (_, _) => ApplyIfZoomed();
    }

    public bool IsZoomed => _zoom.IsZoomed;
    public double Scale => _zoom.Scale;
    /// <summary>True between <see cref="BeginPan"/> on a zoomed image and <see cref="EndPan"/>.</summary>
    public bool IsPanning => _panLast is not null;

    /// <summary>Raised after every change of the zoom (also on reset).</summary>
    public event Action? Changed;

    /// <summary>Zooms by <paramref name="notches"/> wheel notches (negative = out) keeping the point <paramref name="p"/> fixed.</summary>
    public void WheelAt(Point p, double notches)
    {
        Sync();
        _zoom.WheelAt(p.X, p.Y, notches);
        Apply();
    }

    /// <summary>Starts a pan at <paramref name="p"/> if the image is zoomed; returns whether it did.</summary>
    public bool BeginPan(Point p)
    {
        _panLast = _zoom.IsZoomed ? p : null;
        return _panLast is not null;
    }

    public void PanTo(Point p)
    {
        if (_panLast is not { } last) return;
        _zoom.Pan(p.X - last.X, p.Y - last.Y);
        _panLast = p;
        Apply();
    }

    public void EndPan() => _panLast = null;

    /// <summary>Back to the whole image.</summary>
    public void Reset()
    {
        _panLast = null;
        _zoom.Reset();
        Apply();
    }

    /// <summary>Shows the zoom state: transform and scaling quality, then raises <see cref="Changed"/>.</summary>
    public void Apply()
    {
        var o = Sync();
        if (_zoom.IsZoomed)
        {
            _transform.Matrix = ZoomMatrix(_zoom.Scale, _zoom.OffsetX, _zoom.OffsetY, o);
            if (!ReferenceEquals(_video.RenderTransform, _transform)) _video.RenderTransform = _transform;
        }
        else
        {
            _video.RenderTransform = null;
        }
        RenderOptions.SetBitmapInterpolationMode(_video,
            _zoom.IsZoomed ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.MediumQuality);
        Changed?.Invoke();
    }

    void ApplyIfZoomed()
    {
        if (_zoom.IsZoomed) Apply();
    }

    /// <summary>
    /// Gives the zoom the host size and the image's letterboxed rectangle inside it, so panning never brings the black
    /// bars into view. Returns the surface's layout offset in the host (excludes the render transform).
    /// </summary>
    Point Sync()
    {
        var o = _video.Bounds.Position;
        var content = _video.ContentRect;
        _zoom.Resize(_host.Bounds.Width, _host.Bounds.Height);
        _zoom.SetContent(o.X + content.X, o.Y + content.Y, content.Width, content.Height);
        return o;
    }

    /// <summary>
    /// The surface's render transform: the zoom lives in host coordinates and the surface sits at layout offset
    /// <paramref name="o"/>, so host = o + M(local) must equal scale·(o + local) + offset, i.e. M's translation is
    /// (scale − 1)·o + offset.
    /// </summary>
    internal static Matrix ZoomMatrix(double scale, double offsetX, double offsetY, Point o) =>
        new(scale, 0, 0, scale, (scale - 1) * o.X + offsetX, (scale - 1) * o.Y + offsetY);

    /// <summary>
    /// The decode box in device pixels for a tile of <paramref name="size"/> DIPs: a zoomed image is decoded larger
    /// (the decoder never upscales beyond the native size).
    /// </summary>
    internal static PixelSize DecodeSize(Size size, double renderScaling, double zoomScale) =>
        new((int)(size.Width * renderScaling * zoomScale), (int)(size.Height * renderScaling * zoomScale));
}
