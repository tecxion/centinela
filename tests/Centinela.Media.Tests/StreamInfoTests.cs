using Centinela.Media.Tests.Rtsp;

namespace Centinela.Media.Tests;

public class StreamInfoDescribeTests
{
    [Theory]
    [InlineData(2560, 1440, "hevc", "aac", "2560×1440 · H.265 · audio AAC")]
    [InlineData(640, 360, "h264", null, "640×360 · H.264 · sin audio")]
    [InlineData(1920, 1080, "h264", "pcm_alaw", "1920×1080 · H.264 · audio G.711 A")]
    [InlineData(1920, 1080, "h264", "pcm_mulaw", "1920×1080 · H.264 · audio G.711 µ")]
    [InlineData(1280, 720, "mjpeg", "opus", "1280×720 · MJPEG · audio OPUS")]
    public void Describe(int w, int h, string video, string? audio, string expected) =>
        Assert.Equal(expected, new StreamInfo(w, h, video, audio).Describe());
}

[Collection("rtsp")]
public sealed class StreamInfoTests(RtspTestServer server)
{
    StreamInfo? InfoOf(string path, bool decode)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        using var session = new StreamSession(server.Url(path), decode: decode);
        StreamInfo? raised = null;
        session.InfoAvailable += info => raised = info;
        session.Start();
        TestUtil.WaitFor(() => raised is not null, TimeSpan.FromSeconds(10));
        Assert.Equal(raised, session.Info);
        return raised;
    }

    [SkippableFact]
    public void Video_only_stream_reports_size_codec_and_no_audio()
    {
        var info = InfoOf("open", decode: true);
        Assert.Equal(new StreamInfo(640, 360, "h264", null), info);
    }

    [SkippableFact]
    public void Stream_with_aac_reports_audio()
    {
        Assert.Equal("aac", InfoOf("av", decode: true)?.AudioCodec);
    }

    [SkippableFact]
    public void Remux_session_reports_info_after_opening()
    {
        Assert.Equal(new StreamInfo(640, 360, "h264", "aac"), InfoOf("av", decode: false));
    }
}
