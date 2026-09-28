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

    const int WarmUp = 5;

    static IEnumerable<GrayFrame> Moving(int count) => Enumerable.Range(0, count).Select(i => Square(5 + i * 10));

    /// <summary>The first <see cref="WarmUp"/> frames only seed the background; then N changed frames in a row.</summary>
    [Theory]
    [InlineData(MotionSensitivity.Low, 3)]
    [InlineData(MotionSensitivity.Medium, 2)]
    [InlineData(MotionSensitivity.High, 2)]
    public void Moving_square_is_detected_after_warm_up_and_N_changed_frames(MotionSensitivity sensitivity, int n) =>
        Assert.Equal(WarmUp - 1 + n, FirstMotion(new FrameDiffDetector(sensitivity), Moving(12)));

    [Fact]
    public void Warm_up_frames_never_detect_but_report_the_change()
    {
        var d = new FrameDiffDetector(MotionSensitivity.High);
        var samples = Moving(WarmUp).Select(d.Analyze).ToList();
        Assert.All(samples, s => Assert.False(s.Motion));
        Assert.Equal(0, samples[0].ChangedFraction);
        Assert.All(samples.Skip(1), s => Assert.True(s.ChangedFraction > 0.03));
    }

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
        for (var i = 0; i < WarmUp; i++) d.Analyze(Solid(40));
        var flash = d.Analyze(Solid(200));
        Assert.False(flash.Motion);
        Assert.True(flash.ChangedFraction > 0.6);
        Assert.Equal(-1, FirstMotion(d, Enumerable.Repeat(0, 10).Select(_ => Solid(200))));
    }

    /// <summary>First images of a new connection: a gray fill and half-decoded (concealed) frames, then the real scene.</summary>
    [Fact]
    public void Junk_frames_after_reset_never_detect()
    {
        var d = new FrameDiffDetector(MotionSensitivity.High);
        foreach (var f in Enumerable.Repeat(0, 10).Select(_ => Square(40))) d.Analyze(f);
        d.Reset();
        GrayFrame Concealed()
        {
            var f = Square(40);
            Array.Fill(f.Pixels, (byte)128, f.Pixels.Length / 2, f.Pixels.Length / 2);
            return f;
        }
        var frames = new[] { Solid(128), Concealed(), Concealed() }.Concat(Enumerable.Repeat(0, 20).Select(_ => Square(40)));
        Assert.Equal(-1, FirstMotion(d, frames));
    }

    /// <summary>Day/night switch: a global change, then the exposure settles over a few frames (~10 % each).</summary>
    [Fact]
    public void Exposure_ramp_after_a_global_change_never_detects()
    {
        var d = new FrameDiffDetector(MotionSensitivity.High);
        for (var i = 0; i < WarmUp; i++) d.Analyze(Solid(40));
        Assert.False(d.Analyze(Solid(200)).Motion);
        GrayFrame Ramp(int step)
        {
            // The top step×10 % of the rows have settled to 160 (a change of 40 from 200).
            var f = Solid(200);
            Array.Fill(f.Pixels, (byte)160, 0, f.Pixels.Length * step / 10);
            return f;
        }
        var frames = Enumerable.Range(1, 4).Select(Ramp).Concat(Enumerable.Repeat(0, 20).Select(_ => Ramp(4)));
        Assert.Equal(-1, FirstMotion(d, frames));
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
