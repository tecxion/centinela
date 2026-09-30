using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Quality;

namespace Centinela.Desktop.Tests;

public class QualityEditsTests
{
    static readonly IReadOnlyList<(int W, int H)> Choices = [(1920, 1080), (1280, 720), (640, 360)];

    static EncoderOptions Options(int fpsMin, int fpsMax) => new([], fpsMin, fpsMax, 0, 0);

    [Fact]
    public void Recommended_main_stream_is_720p_at_10_fps_and_768_kbps()
    {
        Assert.Equal(new EncoderChange(1280, 720, 10, 768), QualityEdits.Recommended(Choices, null, main: true));
    }

    [Fact]
    public void Recommended_sub_stream_is_360p_at_10_fps_and_256_kbps()
    {
        Assert.Equal(new EncoderChange(640, 360, 10, 256), QualityEdits.Recommended(Choices, null, main: false));
    }

    [Fact]
    public void Recommended_takes_the_smallest_when_none_fits()
    {
        IReadOnlyList<(int W, int H)> big = [(2560, 1440), (1920, 1080)];
        Assert.Equal((1920, 1080), Size(QualityEdits.Recommended(big, null, main: false)));
    }

    [Fact]
    public void Recommended_fps_is_limited_by_the_camera()
    {
        Assert.Equal(8, QualityEdits.Recommended(Choices, Options(1, 8), main: true).Fps);
    }

    [Fact]
    public void FpsRange_defaults_to_1_to_30()
    {
        Assert.Equal((1, 30), QualityEdits.FpsRange(null));
        Assert.Equal((1, 30), QualityEdits.FpsRange(Options(0, 0)));
        Assert.Equal((5, 25), QualityEdits.FpsRange(Options(5, 25)));
    }

    [Fact]
    public void TryRead_rejects_non_numeric_values()
    {
        var error = QualityEdits.TryRead("000", (1280, 720), "abc", "768", null, out _);
        Assert.Equal("Revisa los valores de 000: fps y bitrate deben ser números enteros.", error);
    }

    [Fact]
    public void TryRead_without_a_resolution_asks_to_check_the_values()
    {
        var error = QualityEdits.TryRead("000", null, "10", "768", null, out _);
        Assert.Equal("Revisa los valores de 000: fps y bitrate deben ser números enteros.", error);
    }

    [Fact]
    public void TryRead_rejects_values_out_of_range()
    {
        var error = QualityEdits.TryRead("000", (1280, 720), "40", "768", Options(1, 30), out _);
        Assert.Equal("000: los fps deben estar entre 1 y 30, y el bitrate entre 32 y 16384 kbps.", error);
    }

    [Fact]
    public void TryRead_accepts_valid_values()
    {
        var error = QualityEdits.TryRead("001", (640, 360), " 12 ", "256", Options(1, 30), out var change);
        Assert.Null(error);
        Assert.Equal(new EncoderChange(640, 360, 12, 256), change);
    }

    [Fact]
    public void Choices_puts_the_current_resolution_first_when_not_offered()
    {
        var current = new EncoderSettings("Main", "H264", 2304, 1296, 15, 2048, null, 30);
        var options = new EncoderOptions([(1920, 1080), (1280, 720)], 1, 25, 32, 8192);
        Assert.Equal([(2304, 1296), (1920, 1080), (1280, 720)], QualityEdits.Choices(current, options));
        Assert.Equal([(1920, 1080), (1280, 720)],
            QualityEdits.Choices(current with { Width = 1280, Height = 720 }, options));
    }

    static (int, int) Size(EncoderChange c) => (c.Width, c.Height);
}
