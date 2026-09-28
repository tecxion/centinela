using Centinela.Core;

namespace Centinela.Core.Tests;

public class FrameDiffDetectorTests
{
    static GrayFrame Solid(byte value)
    {
        var f = GrayScaler.Create();
        Array.Fill(f.Pixels, value);
        return f;
    }

    /// <summary>Background 50 with a 30×30 square of 200 whose left edge is at <paramref name="x"/>.</summary>
    static GrayFrame Square(int x)
    {
        var f = Solid(50);
        for (var yy = 30; yy < 60; yy++)
            for (var xx = x; xx < x + 30; xx++)
                f.Pixels[yy * 160 + xx] = 200;
        return f;
    }

    static int FirstMotion(IMotionDetector d, IEnumerable<GrayFrame> frames)
    {
        var i = 0;
        foreach (var f in frames)
        {
            if (d.Analyze(f).Motion) return i;
            i++;
        }
        return -1;
    }

    static IEnumerable<GrayFrame> Moving(int count) => Enumerable.Range(0, count).Select(i => Square(5 + i * 15));

    [Theory]
    [InlineData(MotionSensitivity.Low, 3)]
    [InlineData(MotionSensitivity.Medium, 2)]
    [InlineData(MotionSensitivity.High, 2)]
    public void Moving_square_is_detected_after_N_changed_frames(MotionSensitivity sensitivity, int expectedIndex) =>
        Assert.Equal(expectedIndex, FirstMotion(new FrameDiffDetector(sensitivity), Moving(8)));

    [Fact]
    public void First_frame_never_detects()
    {
        var sample = new FrameDiffDetector(MotionSensitivity.High).Analyze(Square(5));
        Assert.Equal((false, 0.0), (sample.Motion, sample.ChangedFraction));
    }

    [Fact]
    public void Static_scene_never_detects() =>
        Assert.Equal(-1, FirstMotion(new FrameDiffDetector(MotionSensitivity.High), Enumerable.Repeat(0, 30).Select(_ => Square(40))));

    [Fact]
    public void Sensor_noise_never_detects()
    {
        var random = new Random(7);
        var frames = Enumerable.Range(0, 40).Select(_ =>
        {
            var f = GrayScaler.Create();
            for (var i = 0; i < f.Pixels.Length; i++) f.Pixels[i] = (byte)(100 + random.Next(-10, 11));
            return f;
        });
        Assert.Equal(-1, FirstMotion(new FrameDiffDetector(MotionSensitivity.High), frames));
    }

    [Fact]
    public void Global_light_change_is_ignored_and_resets_the_background()
    {
        var d = new FrameDiffDetector(MotionSensitivity.High);
        d.Analyze(Solid(40));
        var flash = d.Analyze(Solid(200));
        Assert.False(flash.Motion);
        Assert.True(flash.ChangedFraction > 0.6);
        Assert.Equal(-1, FirstMotion(d, Enumerable.Repeat(0, 10).Select(_ => Solid(200))));
    }

    [Fact]
    public void Reset_starts_over()
    {
        var d = new FrameDiffDetector(MotionSensitivity.Medium);
        foreach (var f in Moving(3)) d.Analyze(f);
        d.Reset();
        Assert.False(d.Analyze(Square(80)).Motion);
    }
}
