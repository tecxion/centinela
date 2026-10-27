namespace Centinela.Core;

/// <summary>
/// Digital zoom of a view: screen = point × Scale + Offset, with points in unzoomed view coordinates.
/// The image (the content rectangle inside the view, e.g. a letterboxed picture) always covers the view
/// on each axis where it is big enough, and is centred on an axis where it is still smaller.
/// </summary>
public sealed class ZoomState
{
    public const double MinScale = 1, MaxScale = 8, Step = 1.25;
    const double Epsilon = 1e-6;

    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public double ViewWidth { get; private set; }
    public double ViewHeight { get; private set; }
    public bool IsZoomed => Scale > MinScale + Epsilon;

    double _contentX, _contentY, _contentWidth, _contentHeight;
    bool _hasContent;

    /// <summary>
    /// Where the image lies inside the view (unzoomed view coordinates). An empty rectangle means the whole view,
    /// which is also the default.
    /// </summary>
    public void SetContent(double x, double y, double width, double height)
    {
        _hasContent = width > 0 && height > 0;
        (_contentX, _contentY, _contentWidth, _contentHeight) = (x, y, width, height);
        Clamp();
    }

    public void Resize(double width, double height)
    {
        ViewWidth = Math.Max(0, width);
        ViewHeight = Math.Max(0, height);
        Clamp();
    }

    /// <summary>Zooms by <see cref="Step"/> per notch (negative = out) keeping the screen point (x, y) fixed.</summary>
    public void WheelAt(double x, double y, double notches)
    {
        var scale = Math.Clamp(Scale * Math.Pow(Step, notches), MinScale, MaxScale);
        if (scale <= MinScale + Epsilon)
        {
            Reset();
            return;
        }
        var factor = scale / Scale;
        OffsetX = x - (x - OffsetX) * factor;
        OffsetY = y - (y - OffsetY) * factor;
        Scale = scale;
        Clamp();
    }

    public void Pan(double dx, double dy)
    {
        if (!IsZoomed) return;
        OffsetX += dx;
        OffsetY += dy;
        Clamp();
    }

    public void Reset()
    {
        Scale = MinScale;
        OffsetX = OffsetY = 0;
    }

    void Clamp()
    {
        if (!IsZoomed)
        {
            OffsetX = OffsetY = 0;
            return;
        }
        var (x, y, w, h) = _hasContent ? (_contentX, _contentY, _contentWidth, _contentHeight) : (0, 0, ViewWidth, ViewHeight);
        OffsetX = ClampAxis(OffsetX, x, w, ViewWidth);
        OffsetY = ClampAxis(OffsetY, y, h, ViewHeight);
    }

    /// <summary>The scaled content [start, start + size]·Scale + offset covers [0, view], or is centred if too small.</summary>
    double ClampAxis(double offset, double start, double size, double view)
    {
        if (size * Scale < view) return (view - size * Scale) / 2 - start * Scale;
        return Math.Clamp(offset, view - (start + size) * Scale, -start * Scale);
    }
}
