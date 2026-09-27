using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Centinela.Media;

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

    /// <summary>Removes the sink only if it is still the one registered for the context (addresses are reused).</summary>
    public static void Unregister(void* context, Action<string> sink) =>
        Sinks.TryRemove(new KeyValuePair<nint, Action<string>>((nint)context, sink));

    /// <summary>Delivers a formatted line to the sink registered for the context, if any.</summary>
    internal static void Route(nint context, string line)
    {
        if (!Sinks.TryGetValue(context, out var sink)) return;
        try { sink(line); }
        catch (Exception) { /* never let a sink break FFmpeg's thread */ }
    }

    static void OnLog(void* avcl, int level, string format, byte* vl)
    {
        var lvl = level & 0xff; // upper bits carry flags/colour
        if (lvl > ffmpeg.AV_LOG_WARNING || avcl is null || !Sinks.ContainsKey((nint)avcl)) return;
        const int size = 1024;
        var buffer = stackalloc byte[size];
        var printPrefix = 1;
        ffmpeg.av_log_format_line(avcl, level, format, vl, buffer, size, &printPrefix);
        var line = Marshal.PtrToStringUTF8((nint)buffer)?.Trim();
        if (string.IsNullOrEmpty(line)) return;
        Route((nint)avcl, line);
    }
}
