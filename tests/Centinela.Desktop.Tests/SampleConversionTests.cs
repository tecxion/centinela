using Centinela.Desktop.Services.Audio;

namespace Centinela.Desktop.Tests;

public class SampleConversionTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 32767)]
    [InlineData(-1f, -32767)]
    [InlineData(2f, 32767)]
    [InlineData(-3f, -32767)]
    [InlineData(0.5f, 16384)]
    [InlineData(-0.5f, -16384)]
    [InlineData(float.NaN, 0)]
    public void Converts_float_samples_to_clipped_pcm16(float sample, short expected)
    {
        var target = new short[1];
        SampleConversion.ToPcm16([sample], target);
        Assert.Equal(expected, target[0]);
    }

    [Fact]
    public void Converts_every_interleaved_sample_and_leaves_the_rest_of_the_target()
    {
        var target = new short[] { 7, 7, 7, 7, 7 };
        SampleConversion.ToPcm16([0f, 1f, -1f, 0.25f], target);
        Assert.Equal([0, 32767, -32767, 8192, 7], target);
    }

    [Fact]
    public void Rejects_a_target_shorter_than_the_source() =>
        Assert.Throws<ArgumentException>(() => SampleConversion.ToPcm16([0f, 0f], new short[1]));
}
