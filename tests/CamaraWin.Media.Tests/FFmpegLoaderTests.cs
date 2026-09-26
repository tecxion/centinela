using CamaraWin.Media;
using FFmpeg.AutoGen;

namespace CamaraWin.Media.Tests;

public class FFmpegLoaderTests
{
    public FFmpegLoaderTests() => FFmpegLoader.Initialize();

    [Fact]
    public void Loaded_avcodec_major_matches_bindings() =>
        Assert.Equal(ffmpeg.LibraryVersionMap["avcodec"], (int)(ffmpeg.avcodec_version() >> 16));

    [Fact]
    public void Version_is_reported() =>
        Assert.False(string.IsNullOrWhiteSpace(FFmpegLoader.Version));

    [Fact]
    public void Describe_translates_error_codes() =>
        Assert.Contains("End of file", FFmpegException.Describe(ffmpeg.AVERROR_EOF));

    [Fact]
    public void Unauthorized_is_an_auth_error() =>
        Assert.True(new FFmpegException(ffmpeg.AVERROR_HTTP_UNAUTHORIZED, "open").IsAuthError);

    [Fact]
    public void ThrowIfError_passes_through_non_negative() =>
        Assert.Equal(3, FFmpegException.ThrowIfError(3, "x"));

    [Fact]
    public void ThrowIfError_throws_on_negative() =>
        Assert.Throws<FFmpegException>(() => FFmpegException.ThrowIfError(ffmpeg.AVERROR_EOF, "read"));
}
