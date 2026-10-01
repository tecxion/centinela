using Centinela.Desktop.Services.Audio;

namespace Centinela.Desktop.Tests;

public class NoticeTonesTests
{
    [Fact]
    public void Connection_lost_is_two_tones_of_150_ms()
    {
        Assert.Equal(48000, NoticeTones.SampleRate);
        Assert.Equal(14_400, NoticeTones.ConnectionLost().Length);
    }

    [Fact]
    public void Motion_is_two_80_ms_beeps_with_60_ms_of_silence()
    {
        var samples = NoticeTones.Motion();
        Assert.Equal(10_560, samples.Length);
        // The gap between the beeps is silent.
        Assert.All(samples[3840..6720], s => Assert.Equal(0f, s));
    }

    public static TheoryData<string> Tones => ["lost", "motion"];

    static float[] Of(string name) => name == "lost" ? NoticeTones.ConnectionLost() : NoticeTones.Motion();

    [Theory]
    [MemberData(nameof(Tones))]
    public void Peaks_at_half_amplitude(string name)
    {
        var samples = Of(name);
        var peak = samples.Max(MathF.Abs);
        Assert.True(peak <= 0.5f, $"peak {peak}");
        Assert.True(peak > 0.45f, $"peak {peak}");
    }

    [Theory]
    [MemberData(nameof(Tones))]
    public void Starts_and_ends_in_silence(string name)
    {
        var samples = Of(name);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(0f, samples[^1]);
    }

    [Theory]
    [MemberData(nameof(Tones))]
    public void Fades_in_over_5_ms(string name)
    {
        var samples = Of(name);
        // 5 ms = 240 samples: the first 2 ms stay well under the peak.
        Assert.All(samples[..96], s => Assert.True(MathF.Abs(s) <= 0.5f * 96 / 240 + 1e-6f));
    }
}
