namespace Centinela.Media;

/// <summary>What a camera stream carries. Codec names are FFmpeg's (h264, hevc, aac, pcm_alaw…).</summary>
public sealed record StreamInfo(int Width, int Height, string VideoCodec, string? AudioCodec)
{
    public string Describe() =>
        $"{Width}×{Height} · {DisplayCodec(VideoCodec)} · {(AudioCodec is { } a ? $"audio {DisplayCodec(a)}" : "sin audio")}";

    public static string DisplayCodec(string name) => name switch
    {
        "h264" => "H.264",
        "hevc" => "H.265",
        "aac" => "AAC",
        "pcm_alaw" => "G.711 A",
        "pcm_mulaw" => "G.711 µ",
        _ => name.ToUpperInvariant(),
    };
}
