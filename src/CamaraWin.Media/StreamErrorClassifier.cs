using CamaraWin.Core;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public static class StreamErrorClassifier
{
    // FFmpeg maps Winsock errors to UCRT errno values on Windows (ETIMEDOUT observed as -138).
    const int Etimedout = 138, Econnrefused = 107, Ehostunreach = 110, Enetunreach = 118,
        Eaddrnotavail = 101, Econnreset = 108, Eacces = 13;

    public static StreamErrorKind Classify(int ffmpegCode, int? rtspStatus, bool wasPlaying, bool deadlineHit)
    {
        if (rtspStatus is { } status)
        {
            return status switch
            {
                401 or 403 => StreamErrorKind.AuthFailed,
                404 or 454 => StreamErrorKind.NotFound,
                453 or 503 => StreamErrorKind.CameraBusy,
                >= 500 => StreamErrorKind.ServerError,
                _ => StreamErrorKind.Unknown,
            };
        }
        if (ffmpegCode == ffmpeg.AVERROR_HTTP_UNAUTHORIZED || ffmpegCode == ffmpeg.AVERROR_HTTP_FORBIDDEN || ffmpegCode == -Eacces)
            return StreamErrorKind.AuthFailed;
        if (ffmpegCode == ffmpeg.AVERROR_HTTP_NOT_FOUND) return StreamErrorKind.NotFound;
        if (ffmpegCode == ffmpeg.AVERROR_HTTP_SERVER_ERROR) return StreamErrorKind.ServerError;
        if (deadlineHit || ffmpegCode == ffmpeg.AVERROR_EXIT)
            return wasPlaying ? StreamErrorKind.Stalled : StreamErrorKind.Unreachable;
        if (-ffmpegCode is Etimedout or Econnrefused or Ehostunreach or Enetunreach or Eaddrnotavail or Econnreset
            || ffmpegCode == ffmpeg.AVERROR_EOF)
            return StreamErrorKind.Unreachable;
        return StreamErrorKind.Unknown;
    }
}
