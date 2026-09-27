namespace Centinela.Media;

public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
}

/// <summary>Receives decoded audio: interleaved stereo float32 at 48 kHz. Called on the session's audio thread.</summary>
public interface IAudioSink
{
    void Write(ReadOnlySpan<float> interleavedStereo);
}
