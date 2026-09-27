using Centinela.Core;
using Centinela.Media;
using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public class ErrorClassificationTests
{
    public ErrorClassificationTests() => FFmpegLoader.Initialize();

    [Theory]
    [InlineData("[rtsp @ 000001] method DESCRIBE failed: 401 Unauthorized", 401)]
    [InlineData("method SETUP failed: 461 Unsupported transport", 461)]
    [InlineData("method DESCRIBE failed: 404 Not Found", 404)]
    [InlineData("RTSP/1.0 503 Service Unavailable", 503)]
    [InlineData("Connection to tcp://127.0.0.1:1 failed: Error number -138 occurred", null)]
    [InlineData("method OPTIONS failed: 200 OK", null)]
    public void Parses_rtsp_status(string line, int? expected) => Assert.Equal(expected, RtspStatusParser.Parse(line));

    [Theory]
    [InlineData("open rtsp://viewer:p%40ss@127.0.0.1:18554/secure failed", "open rtsp://127.0.0.1:18554/secure failed")]
    [InlineData("a rtsp://u:p@h/x and http://x:y@z/w", "a rtsp://h/x and http://z/w")]
    [InlineData("no credentials rtsp://h/x", "no credentials rtsp://h/x")]
    [InlineData("rtsp://admin:p@ss@host/x", "rtsp://host/x")]
    public void Sanitizer_removes_userinfo(string input, string expected) =>
        Assert.Equal(expected, CredentialSanitizer.Sanitize(input));

    [Theory]
    [InlineData(401, StreamErrorKind.AuthFailed)]
    [InlineData(403, StreamErrorKind.AuthFailed)]
    [InlineData(404, StreamErrorKind.NotFound)]
    [InlineData(454, StreamErrorKind.NotFound)]
    [InlineData(453, StreamErrorKind.CameraBusy)]
    [InlineData(503, StreamErrorKind.CameraBusy)]
    [InlineData(500, StreamErrorKind.ServerError)]
    [InlineData(461, StreamErrorKind.Unknown)]
    public void Status_wins(int status, StreamErrorKind kind) =>
        Assert.Equal(kind, StreamErrorClassifier.Classify(-1, status, wasPlaying: false, deadlineHit: false));

    [Fact]
    public unsafe void Unregister_leaves_a_newer_sink_for_a_reused_address()
    {
        var context = (void*)0x1234_5678;
        var first = new List<string>();
        var second = new List<string>();
        Action<string> s1 = first.Add, s2 = second.Add;
        FFmpegLog.Register(context, s1);
        FFmpegLog.Register(context, s2); // another session got the freed address
        FFmpegLog.Unregister(context, s1);
        FFmpegLog.Route((nint)context, "line");
        FFmpegLog.Unregister(context, s2);
        FFmpegLog.Route((nint)context, "after");
        Assert.Empty(first);
        Assert.Equal(["line"], second);
    }

    [Fact]
    public void Ffmpeg_codes_classify()
    {
        Assert.Equal(StreamErrorKind.AuthFailed, StreamErrorClassifier.Classify(ffmpeg.AVERROR_HTTP_UNAUTHORIZED, null, false, false));
        Assert.Equal(StreamErrorKind.AuthFailed, StreamErrorClassifier.Classify(-13, null, false, false));
        Assert.Equal(StreamErrorKind.NotFound, StreamErrorClassifier.Classify(ffmpeg.AVERROR_HTTP_NOT_FOUND, null, false, false));
        Assert.Equal(StreamErrorKind.ServerError, StreamErrorClassifier.Classify(ffmpeg.AVERROR_HTTP_SERVER_ERROR, null, false, false));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(-138, null, false, false));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(-107, null, false, false));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(ffmpeg.AVERROR_EXIT, null, false, true));
        Assert.Equal(StreamErrorKind.Stalled, StreamErrorClassifier.Classify(ffmpeg.AVERROR_EXIT, null, true, true));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(ffmpeg.AVERROR_EOF, null, true, false));
        Assert.Equal(StreamErrorKind.Unknown, StreamErrorClassifier.Classify(-22, null, false, false));
    }
}
