using Centinela.Core;

namespace Centinela.Core.Tests;

public class ZoomStateTests
{
    static ZoomState View(double w = 800, double h = 450)
    {
        var z = new ZoomState();
        z.Resize(w, h);
        return z;
    }

    [Fact]
    public void Starts_unzoomed() => Assert.Equal((1.0, 0.0, 0.0, false), (View().Scale, View().OffsetX, View().OffsetY, View().IsZoomed));

    [Fact]
    public void Wheel_keeps_the_point_under_the_cursor_fixed()
    {
        var z = View();
        z.WheelAt(200, 100, 1);
        var contentX = (200 - z.OffsetX) / z.Scale;
        var contentY = (100 - z.OffsetY) / z.Scale;
        Assert.Equal(1.25, z.Scale, 6);
        Assert.Equal(200, contentX, 6);
        Assert.Equal(100, contentY, 6);
    }

    [Fact]
    public void Scale_is_clamped_between_1_and_8()
    {
        var z = View();
        z.WheelAt(400, 225, 50);
        Assert.Equal(8, z.Scale, 6);
        z.WheelAt(400, 225, -100);
        Assert.Equal((1.0, 0.0, 0.0), (z.Scale, z.OffsetX, z.OffsetY));
        Assert.False(z.IsZoomed);
    }

    [Fact]
    public void Pan_is_clamped_so_the_image_always_covers_the_view()
    {
        var z = View();
        z.WheelAt(0, 0, 4); // scale ≈ 2.44, anchored top-left
        z.Pan(500, 500);
        Assert.Equal((0.0, 0.0), (z.OffsetX, z.OffsetY));
        z.Pan(-10_000, -10_000);
        Assert.Equal(800 * (1 - z.Scale), z.OffsetX, 6);
        Assert.Equal(450 * (1 - z.Scale), z.OffsetY, 6);
    }

    [Fact]
    public void Pan_does_nothing_when_not_zoomed()
    {
        var z = View();
        z.Pan(100, 100);
        Assert.Equal((0.0, 0.0), (z.OffsetX, z.OffsetY));
    }

    [Fact]
    public void Resize_reclamps_and_reset_restores()
    {
        var z = View();
        z.WheelAt(800, 450, 6);
        z.Resize(400, 200);
        Assert.InRange(z.OffsetX, 400 * (1 - z.Scale), 0);
        Assert.InRange(z.OffsetY, 200 * (1 - z.Scale), 0);
        z.Reset();
        Assert.Equal((1.0, 0.0, 0.0), (z.Scale, z.OffsetX, z.OffsetY));
    }
}
