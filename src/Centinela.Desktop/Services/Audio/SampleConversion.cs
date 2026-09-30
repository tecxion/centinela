namespace Centinela.Desktop.Services.Audio;

/// <summary>Float samples (−1…1) to signed 16-bit PCM, the format every OpenAL implementation plays.</summary>
static class SampleConversion
{
    /// <summary>Clips to ±1 and rounds; NaN becomes silence. <paramref name="target"/> must hold every sample.</summary>
    public static void ToPcm16(ReadOnlySpan<float> source, Span<short> target)
    {
        if (target.Length < source.Length)
            throw new ArgumentException("The target is shorter than the source.", nameof(target));
        for (var i = 0; i < source.Length; i++)
        {
            var sample = source[i];
            target[i] = float.IsNaN(sample)
                ? (short)0
                : (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue, MidpointRounding.AwayFromZero);
        }
    }
}
