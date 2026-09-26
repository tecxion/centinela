using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

/// <summary>
/// Replaces FFmpeg's stderr logger. Lines logged against a registered AVFormatContext (warning or worse)
/// are forwarded to that session's sink; everything else is dropped.
/// </summary>
public static unsafe class FFmpegLog
{
    static readonly ConcurrentDictionary<nint, Action<string>> Sinks = new();
    static readonly object Gate = new();
    static av_log_set_callback_callback? _callback; // rooted: FFmpeg keeps the function pointer

    public static void Install()
    {
        lock (Gate)
        {
            if (_callback is not null) return;
            _callback = OnLog;
            ffmpeg.av_log_set_callback(_callback);
        }
    }

    public static void Register(void* context, Action<string> sink) => Sinks[(nint)context] = sink;

    public static void Unregister(void* context) => Sinks.TryRemove((nint)context, out _);

    static void OnLog(void* avcl, int level, string format, byte* vl)
    {
        if (level > ffmpeg.AV_LOG_WARNING || avcl is null || !Sinks.TryGetValue((nint)avcl, out var sink)) return;
        const int size = 1024;
        var buffer = stackalloc byte[size];
        var printPrefix = 1;
        ffmpeg.av_log_format_line(avcl, level, format, vl, buffer, size, &printPrefix);
        var line = Marshal.PtrToStringUTF8((nint)buffer)?.Trim();
        if (string.IsNullOrEmpty(line)) return;
        try { sink(line); }
        catch (Exception) { /* never let a sink break FFmpeg's thread */ }
    }
}
