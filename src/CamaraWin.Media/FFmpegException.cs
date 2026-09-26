using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public sealed unsafe class FFmpegException(int errorCode, string operation)
    : Exception($"{operation}: {Describe(errorCode)} ({errorCode})")
{
    const int AverrorEacces = -13;

    public int ErrorCode { get; } = errorCode;

    public bool IsAuthError =>
        ErrorCode == ffmpeg.AVERROR_HTTP_UNAUTHORIZED
        || ErrorCode == ffmpeg.AVERROR_HTTP_FORBIDDEN
        || ErrorCode == AverrorEacces;

    public static string Describe(int error)
    {
        const int size = 256;
        var buffer = stackalloc byte[size];
        ffmpeg.av_strerror(error, buffer, size);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"error {error}";
    }

    public static int ThrowIfError(int result, string operation) =>
        result < 0 ? throw new FFmpegException(result, operation) : result;
}
