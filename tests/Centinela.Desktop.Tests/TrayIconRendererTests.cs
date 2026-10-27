using Centinela.Desktop.Services.Tray;

namespace Centinela.Desktop.Tests;

public class TrayIconRendererTests
{
    [Fact]
    public void Half_transparent_pixels_are_divided_by_their_alpha()
    {
        byte[] bgra = [64, 32, 0, 128];
        TrayIconRenderer.Unpremultiply(bgra);
        Assert.InRange(bgra[0], 127, 128);
        Assert.InRange(bgra[1], 63, 64);
        Assert.Equal(0, bgra[2]);
        Assert.Equal(128, bgra[3]);
    }

    [Fact]
    public void Transparent_pixels_become_all_zero()
    {
        byte[] bgra = [10, 20, 30, 0];
        TrayIconRenderer.Unpremultiply(bgra);
        Assert.Equal([0, 0, 0, 0], bgra);
    }

    [Fact]
    public void Opaque_pixels_do_not_change_and_every_pixel_is_handled()
    {
        byte[] bgra = [1, 2, 3, 255, 64, 64, 64, 128];
        TrayIconRenderer.Unpremultiply(bgra);
        Assert.Equal([1, 2, 3, 255], bgra[..4]);
        Assert.InRange(bgra[4], 127, 128);
    }

    [Fact]
    public void Channels_never_go_past_255()
    {
        // Not valid premultiplied data, but rounding must not wrap around.
        byte[] bgra = [200, 0, 0, 100];
        TrayIconRenderer.Unpremultiply(bgra);
        Assert.Equal(255, bgra[0]);
    }
}
