using Centinela.Core;
using Centinela.Core.Platform;
using FFmpeg.AutoGen;

namespace Centinela.Media;

public static class StreamErrorClassifier
{
    const int Eacces = 13;

    // FFmpeg returns -errno: ETIMEDOUT, ECONNREFUSED, EHOSTUNREACH, ENETUNREACH, EADDRNOTAVAIL, ECONNRESET (EHOSTDOWN).
    static readonly HashSet<int> WindowsErrno = [138, 107, 110, 118, 101, 108]; // UCRT values FFmpeg maps Winsock errors to (ETIMEDOUT observed as -138)
    static readonly HashSet<int> LinuxErrno = [110, 111, 113, 101, 99, 104, 112]; // Linux errno
    static readonly HashSet<int> MacErrno = [60, 61, 65, 51, 49, 54, 64]; // BSD errno on macOS

    static IReadOnlySet<int> UnreachableErrno(OsKind os) => os switch
    {
        OsKind.Windows => WindowsErrno,
        OsKind.MacOS => MacErrno,
        _ => LinuxErrno,
    };

    public static StreamErrorKind Classify(int ffmpegCode, int? rtspStatus, bool wasPlaying, bool deadlineHit) =>
        Classify(ffmpegCode, rtspStatus, wasPlaying, deadlineHit, OsInfo.Current);

    public static StreamErrorKind Classify(int ffmpegCode, int? rtspStatus, bool wasPlaying, bool deadlineHit, OsKind os)
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
        if (UnreachableErrno(os).Contains(-ffmpegCode) || ffmpegCode == ffmpeg.AVERROR_EOF)
            return StreamErrorKind.Unreachable;
        return StreamErrorKind.Unknown;
    }
}
