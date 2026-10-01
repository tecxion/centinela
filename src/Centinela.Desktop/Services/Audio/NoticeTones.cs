namespace Centinela.Desktop.Services.Audio;

/// <summary>
/// The notice sounds, synthesized: mono float samples at <see cref="SampleRate"/>. Each tone fades in and out over
/// 5 ms (no clicks) and peaks at half amplitude.
/// </summary>
static class NoticeTones
{
    public const int SampleRate = 48000;
    const float Amplitude = 0.5f;
    const int FadeSamples = SampleRate * 5 / 1000;

    /// <summary>Two falling tones: 880 Hz then 660 Hz, 150 ms each.</summary>
    public static float[] ConnectionLost()
    {
        var samples = new float[Samples(150) * 2];
        Tone(samples.AsSpan(0, Samples(150)), 880);
        Tone(samples.AsSpan(Samples(150)), 660);
        return samples;
    }

    /// <summary>Two short 1000 Hz beeps of 80 ms with 60 ms of silence between them.</summary>
    public static float[] Motion()
    {
        int beep = Samples(80), gap = Samples(60);
        var samples = new float[beep * 2 + gap];
        Tone(samples.AsSpan(0, beep), 1000);
        Tone(samples.AsSpan(beep + gap), 1000);
        return samples;
    }

    static int Samples(int milliseconds) => SampleRate * milliseconds / 1000;

    /// <summary>A sine filling <paramref name="target"/>, with a linear fade at each end (first and last sample 0).</summary>
    static void Tone(Span<float> target, double frequency)
    {
        var last = target.Length - 1;
        for (var i = 0; i < target.Length; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, last - i) / (double)FadeSamples);
            target[i] = (float)(Amplitude * envelope * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }
    }
}
