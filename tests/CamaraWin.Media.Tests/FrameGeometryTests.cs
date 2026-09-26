using CamaraWin.Media;

namespace CamaraWin.Media.Tests;

public class FrameGeometryTests
{
    [Theory]
    [InlineData(640, 360, 0, 0, 640, 360)]
    [InlineData(640, 360, 320, 320, 320, 180)]
    [InlineData(1920, 1080, 1000, 1000, 1000, 562)]
    [InlineData(640, 360, 1280, 720, 640, 360)]
    [InlineData(640, 360, 641, 100, 178, 100)]
    public void FitSize_keeps_aspect_never_upscales_and_is_even(int sw, int sh, int bw, int bh, int w, int h) =>
        Assert.Equal((w, h), FrameGeometry.FitSize(sw, sh, bw, bh));
}
