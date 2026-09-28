using Centinela.Core;

namespace Centinela.Core.Tests;

public class GrayScalerTests
{
    static byte[] Bgra(int w, int h, Func<int, int, (byte B, byte G, byte R)> color)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (b, g, r) = color(x, y);
                var i = (y * w + x) * 4;
                (data[i], data[i + 1], data[i + 2], data[i + 3]) = (b, g, r, 255);
            }
        return data;
    }

    [Fact]
    public void Output_is_160x90()
    {
        var frame = GrayScaler.Create();
        Assert.Equal((160, 90, 160 * 90), (frame.Width, frame.Height, frame.Pixels.Length));
    }

    [Fact]
    public void Solid_color_becomes_its_luma()
    {
        var frame = GrayScaler.Create();
        GrayScaler.FromBgra(Bgra(320, 180, (_, _) => (0, 0, 255)), 320, 180, 320 * 4, frame); // pure red
        Assert.All(frame.Pixels, p => Assert.InRange(p, 74, 78)); // ≈ 0.299·255
    }

    [Fact]
    public void Left_black_right_white_keeps_its_halves()
    {
        var frame = GrayScaler.Create();
        GrayScaler.FromBgra(Bgra(640, 360, (x, _) => x < 320 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255)), 640, 360, 640 * 4, frame);
        Assert.Equal(0, frame.Pixels[10 * 160 + 10]);
        Assert.InRange(frame.Pixels[10 * 160 + 150], 250, 255);
    }

    [Fact]
    public void Works_with_a_padded_stride_and_small_sources()
    {
        var frame = GrayScaler.Create();
        var src = new byte[100 * 4 * 50 + 64];
        GrayScaler.FromBgra(src, 90, 50, 100 * 4, frame); // stride wider than width·4, source smaller than 160×90
        Assert.All(frame.Pixels, p => Assert.Equal(0, p));
    }
}
