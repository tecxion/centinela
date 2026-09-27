namespace Centinela.Core;

/// <summary>Digital zoom of a view: screen = content × Scale + Offset. The image always covers the view.</summary>
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
        OffsetX = Math.Clamp(OffsetX, ViewWidth * (1 - Scale), 0);
        OffsetY = Math.Clamp(OffsetY, ViewHeight * (1 - Scale), 0);
    }
}
