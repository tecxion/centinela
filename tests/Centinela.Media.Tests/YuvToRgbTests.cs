using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

/// <summary>The graphics card's colour matrix must give the same colours as swscale on the processor.</summary>
public unsafe class YuvToRgbTests
{
    static readonly (byte Y, byte U, byte V)[] Samples =
        [(63, 102, 240), (235, 128, 128), (16, 128, 128), (128, 60, 200), (180, 200, 50), (100, 128, 128)];

    /// <summary>The matrix applied to one sample (0–255 each), clamped like the shader; 0–255 out.</summary>
    static (double R, double G, double B) Apply(float[] m, byte y, byte u, byte v)
    {
        double Y = y / 255.0, U = u / 255.0, V = v / 255.0;
        double Row(int r) => Math.Clamp(m[r] * Y + m[4 + r] * U + m[8 + r] * V + m[12 + r], 0, 1) * 255;
        return (Row(0), Row(1), Row(2));
    }

    [Theory]
    [InlineData(YuvMatrix.Bt601, false)]
    [InlineData(YuvMatrix.Bt601, true)]
    [InlineData(YuvMatrix.Bt709, false)]
    [InlineData(YuvMatrix.Bt709, true)]
    [InlineData(YuvMatrix.Bt2020, false)]
    [InlineData(YuvMatrix.Bt2020, true)]
    public void Matrix_matches_swscale(YuvMatrix matrix, bool fullRange)
    {
        var space = matrix switch
        {
            YuvMatrix.Bt709 => AVColorSpace.AVCOL_SPC_BT709,
            YuvMatrix.Bt2020 => AVColorSpace.AVCOL_SPC_BT2020_NCL,
            _ => AVColorSpace.AVCOL_SPC_BT470BG,
        };
        var range = fullRange ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG;
        var m = YuvToRgb.Matrix(matrix, fullRange);
        foreach (var (y, u, v) in Samples)
        {
            var src = FrameConverterTests.Nv12(64, 64, y, u, v, space, range);
            try
            {
                var bgra = new VideoFrame(32, 32);
                using var converter = new FrameConverter();
                converter.Convert(src, bgra);
                var (r, g, b) = Apply(m, y, u, v);
                var sample = $"{matrix} full={fullRange} yuv=({y},{u},{v})";
                Assert.True(Math.Abs(bgra.Data[2] - r) <= 3, $"R {bgra.Data[2]} vs {r:0.0} {sample}");
                Assert.True(Math.Abs(bgra.Data[1] - g) <= 3, $"G {bgra.Data[1]} vs {g:0.0} {sample}");
                Assert.True(Math.Abs(bgra.Data[0] - b) <= 3, $"B {bgra.Data[0]} vs {b:0.0} {sample}");
            }
            finally { ffmpeg.av_frame_free(&src); }
        }
    }

    [Fact]
    public void Limited_range_white_and_black_reach_the_ends()
    {
        var m = YuvToRgb.Matrix(YuvMatrix.Bt709, fullRange: false);
        Assert.Equal((255.0, 255.0, 255.0), Round(Apply(m, 235, 128, 128)));
        Assert.Equal((0.0, 0.0, 0.0), Round(Apply(m, 16, 128, 128)));
    }

    static (double, double, double) Round((double R, double G, double B) c) =>
        (Math.Round(c.R), Math.Round(c.G), Math.Round(c.B));
}
