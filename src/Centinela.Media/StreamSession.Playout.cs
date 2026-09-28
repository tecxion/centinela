using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Centinela.Core.Playback;
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Playout: the connection's reader thread queues every packet and a playout thread decodes and shows them.
/// Normally a packet plays as soon as it is queued (no added latency). When the link turns irregular
/// (<see cref="JitterMonitor"/>), the session switches to smoothing for good: a <see cref="PlayoutClock"/>
/// plays the packets at their own timestamps, 0.5–2 s behind, so bursts come out evenly. Audio packets follow
/// the video packet queued before them, so both keep their sync.
/// </summary>
public sealed unsafe partial class StreamSession
{
    const double MinSmoothingSeconds = 0.5;
    const double MaxSmoothingSeconds = 2.0;
    // Beyond this the decoder cannot keep up (e.g. software decoding of a large stream): drop to the next keyframe.
    const int MaxQueuedPackets = 1000;

    readonly JitterMonitor _jitter = new();
    double _smoothingSeconds; // 0 = off; session-wide so a reconnection keeps what was learned

    /// <summary>Delay of the smoothed live view in seconds; 0 while the stream plays as it arrives.</summary>
    public double SmoothingSeconds => Volatile.Read(ref _smoothingSeconds);

    /// <summary>Raised on a session thread when <see cref="SmoothingSeconds"/> changes; subscriber exceptions are isolated.</summary>
    public event Action<double>? SmoothingChanged;

    readonly record struct QueuedPacket(nint Packet, double Key, long Arrival, bool Video);

    /// <summary>One connection's hand-off between the reader and the playout thread.</summary>
    sealed class Playout
    {
        public readonly ConcurrentQueue<QueuedPacket> Queue = new();
        public readonly AutoResetEvent Signal = new(false);
        public volatile bool Stop;
        public volatile bool FlushRequested;
        public volatile Exception? Failure;
        public double Newest = double.NaN;   // key of the newest video packet queued (Volatile)
        public bool AwaitKeyframe;           // reader only
        public Thread? Thread;
    }

    static double Seconds(long stopwatchTicks) => (double)stopwatchTicks / Stopwatch.Frequency;

    /// <summary>Decode timestamp (else presentation) in seconds; NaN when the packet has neither.</summary>
    static double KeyOf(AVStream* stream, AVPacket* pkt)
    {
        var ts = pkt->dts != ffmpeg.AV_NOPTS_VALUE ? pkt->dts : pkt->pts;
        return ts == ffmpeg.AV_NOPTS_VALUE ? double.NaN : ts * ffmpeg.av_q2d(stream->time_base);
    }

    Playout StartPlayout(AVCodecContext* dec, AVFrame* frame, AVFrame* sw)
    {
        _jitter.Reset();
        var playout = new Playout();
        var decoder = (nint)dec;
        var frames = ((nint)frame, (nint)sw);
        playout.Thread = new Thread(() => PlayoutLoop(playout, decoder, frames.Item1, frames.Item2))
        {
            IsBackground = true,
            Name = "StreamSession playout",
        };
        playout.Thread.Start();
        return playout;
    }

    /// <summary>Reader thread. Stops the playout thread and frees what it left queued.</summary>
    static void StopPlayout(Playout? playout)
    {
        if (playout is null) return;
        playout.Stop = true;
        playout.Signal.Set();
        playout.Thread?.Join();
        while (playout.Queue.TryDequeue(out var left)) Free(left);
        playout.Signal.Dispose();
    }

    static void Free(QueuedPacket entry)
    {
        var pkt = (AVPacket*)entry.Packet;
        ffmpeg.av_packet_free(&pkt);
    }

    /// <summary>Reader thread: a playout failure (decode error) ends the connection like a read error.</summary>
    static void ThrowIfPlayoutFailed(Playout playout)
    {
        if (playout.Failure is { } failure) ExceptionDispatchInfo.Throw(failure);
    }

    /// <summary>Reader thread: measures the link, then hands the packet to the playout thread.</summary>
    void EnqueueVideo(Playout playout, AVStream* stream, AVPacket* pkt, long arrival)
    {
        var key = KeyOf(stream, pkt);
        if (!double.IsNaN(key) && SmoothingSeconds == 0 && _jitter.Add(Seconds(arrival), key))
            SetSmoothing(Math.Clamp(_jitter.WorstStall * 1.2, MinSmoothingSeconds, MaxSmoothingSeconds));

        if (playout.FlushRequested) return;
        if (playout.Queue.Count >= MaxQueuedPackets)
        {
            playout.FlushRequested = true;
            playout.AwaitKeyframe = true;
            playout.Signal.Set();
            return;
        }
        if (playout.AwaitKeyframe)
        {
            if ((pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0) return;
            playout.AwaitKeyframe = false;
        }
        if (!double.IsNaN(key)) Volatile.Write(ref playout.Newest, key);
        Enqueue(playout, pkt, key, arrival, video: true);
    }

    /// <summary>Reader thread: audio plays right after the video packet queued before it.</summary>
    void EnqueueAudio(Playout playout, AVPacket* pkt, long arrival)
    {
        if (playout.FlushRequested || playout.AwaitKeyframe) return;
        Enqueue(playout, pkt, Volatile.Read(ref playout.Newest), arrival, video: false);
    }

    static void Enqueue(Playout playout, AVPacket* pkt, double key, long arrival, bool video)
    {
        var clone = ffmpeg.av_packet_clone(pkt);
        if (clone is null) return;
        playout.Queue.Enqueue(new QueuedPacket((nint)clone, key, arrival, video));
        playout.Signal.Set();
    }

    /// <summary>Tests: start smoothed, as if the link had already been found irregular.</summary>
    internal void ForceSmoothing(double seconds) => SetSmoothing(seconds);

    void SetSmoothing(double seconds)
    {
        if (Volatile.Read(ref _smoothingSeconds) == seconds) return;
        Volatile.Write(ref _smoothingSeconds, seconds);
        try { SmoothingChanged?.Invoke(seconds); }
        catch (Exception) { /* isolated like StateChanged */ }
    }

    void PlayoutLoop(Playout playout, nint decoder, nint frame, nint sw)
    {
        var dec = (AVCodecContext*)decoder;
        PlayoutClock? clock = null;
        try
        {
            while (!playout.Stop)
            {
                if (playout.FlushRequested)
                {
                    while (playout.Queue.TryDequeue(out var dropped)) Free(dropped);
                    playout.FlushRequested = false;
                }
                if (clock is null && SmoothingSeconds > 0)
                {
                    clock = new PlayoutClock(SmoothingSeconds, MaxSmoothingSeconds);
                    TimerResolution.Begin();
                }
                var now = Seconds(Stopwatch.GetTimestamp());
                var newest = Volatile.Read(ref playout.Newest);
                if (!playout.Queue.TryPeek(out var head))
                {
                    if (clock is not null && !double.IsNaN(newest))
                    {
                        clock.Idle(newest, now);
                        SetSmoothing(clock.TargetDelay);
                    }
                    playout.Signal.WaitOne(clock is null ? 100 : 20);
                    continue;
                }

                var present = true;
                if (clock is not null && !double.IsNaN(head.Key) && !double.IsNaN(newest))
                {
                    var decision = clock.Next(head.Key, newest, now);
                    SetSmoothing(clock.TargetDelay);
                    if (decision.Action == PlayoutAction.Wait)
                    {
                        playout.Signal.WaitOne(TimeSpan.FromSeconds(Math.Min(decision.WaitSeconds, 0.05)));
                        continue;
                    }
                    present = decision.Action == PlayoutAction.Present;
                }
                if (!playout.Queue.TryDequeue(out var entry)) continue;
                try
                {
                    if (entry.Video)
                    {
                        _packetTimestamp = entry.Arrival;
                        DecodePacket(dec, (AVPacket*)entry.Packet, (AVFrame*)frame, (AVFrame*)sw, present);
                    }
                    else if (present && _audioParameters is not null && _audioSink is not null)
                        OnAudioPacket((AVPacket*)entry.Packet);
                }
                finally
                {
                    Free(entry);
                }
            }
        }
        catch (Exception ex)
        {
            playout.Failure = ex;
            // Wake the reader out of av_read_frame so the connection ends now, not at the next packet.
            Interlocked.Exchange(ref _deadline, 0);
        }
        finally
        {
            if (clock is not null) TimerResolution.End();
        }
    }

    /// <summary>1 ms system timer while a smoothed view paces frames (the default 15.6 ms makes 40 ms frames uneven).</summary>
    static class TimerResolution
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        static extern uint TimeEndPeriod(uint milliseconds);

        public static void Begin() => TimeBeginPeriod(1);
        public static void End() => TimeEndPeriod(1);
    }
}
