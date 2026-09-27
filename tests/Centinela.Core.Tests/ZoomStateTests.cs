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

    // A 16:9 image letterboxed in a tall 960×1000 view: bars of 230 DIP above and below.
    static ZoomState Letterboxed()
    {
        var z = View(960, 1000);
        z.SetContent(0, 230, 960, 540);
        return z;
    }

    static double Notches(double scale) => Math.Log(scale) / Math.Log(ZoomState.Step);

    [Fact]
    public void Letterboxed_content_covers_the_view_after_panning_to_the_extremes()
    {
        var z = Letterboxed();
        z.WheelAt(480, 500, Notches(2));
        Assert.Equal(2, z.Scale, 6);
        foreach (var (dx, dy) in new[] { (10_000.0, 10_000.0), (-10_000.0, -10_000.0), (0.0, 10_000.0), (0.0, -10_000.0) })
        {
            z.Pan(dx, dy);
            var top = 230 * z.Scale + z.OffsetY;
            var bottom = (230 + 540) * z.Scale + z.OffsetY;
            var left = 0 * z.Scale + z.OffsetX;
            var right = 960 * z.Scale + z.OffsetX;
            Assert.True(top <= 1e-6 && bottom >= 1000 - 1e-6, $"vertical bars: top {top}, bottom {bottom}");
            Assert.True(left <= 1e-6 && right >= 960 - 1e-6, $"horizontal bars: left {left}, right {right}");
        }
    }

    [Fact]
    public void Content_smaller_than_the_view_is_centred_on_that_axis()
    {
        var z = Letterboxed();
        z.WheelAt(100, 300, Notches(1.5));
        Assert.Equal(1.5, z.Scale, 6);
        for (var i = 0; i < 2; i++)
        {
            var top = 230 * z.Scale + z.OffsetY;
            Assert.Equal((1000 - 540 * 1.5) / 2, top, 6);
            z.Pan(0, i == 0 ? 500 : -1000);
        }
        // Horizontally the scaled content (1440) is wider than the view, so it still covers it.
        Assert.InRange(z.OffsetX, 960 * (1 - z.Scale), 0);
    }

    [Fact]
    public void Wheel_keeps_the_point_under_the_cursor_fixed_on_letterboxed_content()
    {
        var z = Letterboxed();
        z.WheelAt(480, 500, Notches(2)); // centre of the view and of the content: no clamping needed
        Assert.Equal(480, (480 - z.OffsetX) / z.Scale, 6);
        Assert.Equal(500, (500 - z.OffsetY) / z.Scale, 6);
    }

    [Fact]
    public void Empty_content_falls_back_to_the_whole_view()
    {
        var z = View();
        z.SetContent(0, 0, 0, 0);
        z.WheelAt(0, 0, 4);
        z.Pan(-10_000, -10_000);
        Assert.Equal(800 * (1 - z.Scale), z.OffsetX, 6);
        Assert.Equal(450 * (1 - z.Scale), z.OffsetY, 6);
    }
}
