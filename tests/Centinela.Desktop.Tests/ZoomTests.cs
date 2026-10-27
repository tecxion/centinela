using Avalonia;
using Centinela.Core;
using Centinela.Desktop.Video;
using Centinela.Desktop.ViewModels;
using Centinela.Media;

namespace Centinela.Desktop.Tests;

/// <summary>Digital zoom: the surface transform, the decode size and what the tile shows.</summary>
public class ZoomTests
{
    static TileViewModel NewTile() =>
        new(new Camera { Name = "Patio", Brand = Brand.Custom }, StreamKind.Main, manage: true, null);

    [Fact]
    public void Matrix_maps_a_surface_point_to_scale_times_host_point_plus_offset()
    {
        var o = new Point(10, 20);
        var m = ZoomPanBehavior.ZoomMatrix(2.5, -30, -40, o);
        var local = new Point(100, 50);
        // host = o + M(local) must equal scale·(o + local) + offset.
        var host = o + (Vector)m.Transform(local);
        Assert.Equal(2.5 * (o.X + local.X) - 30, host.X, 6);
        Assert.Equal(2.5 * (o.Y + local.Y) - 40, host.Y, 6);
    }

    [Fact]
    public void Matrix_at_the_host_origin_is_scale_plus_offset()
    {
        var m = ZoomPanBehavior.ZoomMatrix(2, -5, -7, default);
        Assert.Equal(new Matrix(2, 0, 0, 2, -5, -7), m);
    }

    [Fact]
    public void Decode_size_grows_with_render_scaling_and_zoom()
    {
        Assert.Equal(new PixelSize(640, 360), ZoomPanBehavior.DecodeSize(new Size(640, 360), 1, 1));
        Assert.Equal(new PixelSize(960, 540), ZoomPanBehavior.DecodeSize(new Size(640, 360), 1.5, 1));
        Assert.Equal(new PixelSize(1920, 1080), ZoomPanBehavior.DecodeSize(new Size(640, 360), 1.5, 2));
    }

    [Fact]
    public void Zoom_label_uses_spanish_decimals()
    {
        var tile = NewTile();
        tile.ShowZoom(true, 1.5625);
        Assert.True(tile.IsZoomed);
        Assert.Equal("1,6×", tile.ZoomText);
        tile.ShowZoom(true, 2);
        Assert.Equal("2×", tile.ZoomText);
        tile.ShowZoom(false, 1);
        Assert.False(tile.IsZoomed);
    }

    [Fact]
    public void Zoom_follows_big_and_can_be_turned_off()
    {
        var tile = NewTile();
        Assert.False(tile.EnableZoom);
        tile.Big = true;
        Assert.True(tile.EnableZoom);
        tile.EnableZoom = false; // a placeholder covering its replacement
        Assert.False(tile.EnableZoom);
        tile.Big = false;
        Assert.False(tile.EnableZoom);
        tile.Big = true;
        Assert.True(tile.EnableZoom);
    }
}
