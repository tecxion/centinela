using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Centinela.Core.Playback;
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Optional smoothing, switched on per camera by the user (<see cref="Smoothing"/>) for a link that delivers in
/// stalls and bursts. While off, the reader thread decodes each packet as it arrives, exactly as always. While
/// on, the reader queues the packets and a playout thread decodes them evenly spaced at the frame rate measured
/// from arrivals (<see cref="FrameTimeline"/>; camera timestamps are not trusted, some jump at every RTCP sync),
/// 1–2 s behind (<see cref="PlayoutClock"/>). Audio packets follow the video packet queued before them.
/// </summary>
public sealed unsafe partial class StreamSession
{
    const double InitialSmoothingSeconds = 1.0;
    const double MaxSmoothingSeconds = 2.0;
    // Beyond this the decoder cannot keep up (e.g. software decoding of a large stream): drop to the next keyframe.
    const int MaxQueuedPackets = 1000;

    volatile bool _smoothing;
    readonly FrameTimeline _timeline = new(); // reader only
    double _lastPts = double.NaN;             // reader only: camera timestamp of the previous video packet
    double _smoothingSeconds;                 // current delay, 0 = off (Volatile)
    int _rebuffers;                           // times the smoothed view ran dry (Interlocked)

    /// <summary>
    /// Plays the live view 1–2 s behind but evenly paced. Any thread; applies from the next packet, without
    /// reconnecting. Off by default.
    /// </summary>
    public bool Smoothing
    {
        get => _smoothing;
        set => _smoothing = value;
    }

    /// <summary>Delay of the smoothed live view in seconds; 0 while frames show as they arrive.</summary>
    public double SmoothingSeconds => Volatile.Read(ref _smoothingSeconds);

    /// <summary>Raised on a session thread when <see cref="SmoothingSeconds"/> changes; subscriber exceptions are isolated.</summary>
    public event Action<double>? SmoothingChanged;

    readonly record struct QueuedPacket(nint Packet, double Key, long Arrival, bool Video);

    /// <summary>One smoothing period's hand-off between the reader and the playout thread.</summary>
    sealed class Playout
    {
        public readonly ConcurrentQueue<QueuedPacket> Queue = new();
        public readonly AutoResetEvent Signal = new(false);
        public volatile bool Stop;
        public volatile bool FlushRequested;
        public volatile Exception? Failure;
        public double Newest = double.NaN;   // timeline key of the newest video packet queued (Volatile)
        public bool AwaitKeyframe;           // reader only
        public Thread? Thread;
    }

    static double Seconds(long stopwatchTicks) => (double)stopwatchTicks / Stopwatch.Frequency;

    /// <summary>The camera's timestamp, only to tell frames apart (packets of one frame share it); NaN if none.</summary>
    static double TimestampOf(AVPacket* pkt)
    {
        var ts = pkt->pts != ffmpeg.AV_NOPTS_VALUE ? pkt->pts : pkt->dts;
        return ts == ffmpeg.AV_NOPTS_VALUE ? double.NaN : ts;
    }

    /// <summary>Reader thread: starts or stops the playout thread to follow <see cref="Smoothing"/>.</summary>
    void FollowSmoothing(ref Playout? playout, AVCodecContext* dec, AVFrame* frame, AVFrame* sw, ref bool awaitKeyframe)
    {
        if (_smoothing && playout is null)
        {
            _timeline.Reset();
            _lastPts = double.NaN;
            playout = StartPlayout(dec, frame, sw);
            SetSmoothingSeconds(InitialSmoothingSeconds);
        }
        else if (!_smoothing && playout is not null)
        {
            StopPlayout(playout);
            playout = null;
            // Queued packets were dropped: the decoder needs a keyframe before it can show a clean image again.
            awaitKeyframe = true;
            SetSmoothingSeconds(0);
        }
    }

    Playout StartPlayout(AVCodecContext* dec, AVFrame* frame, AVFrame* sw)
    {
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

    /// <summary>Reader thread: places the packet on the arrival timeline and hands it to the playout thread.</summary>
    void EnqueueVideo(Playout playout, AVPacket* pkt, long arrival)
    {
        var pts = TimestampOf(pkt);
        var newFrame = double.IsNaN(pts) || pts != _lastPts;
        _lastPts = pts;
        var key = _timeline.Add(Seconds(arrival), newFrame);

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
        Volatile.Write(ref playout.Newest, key);
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

    void SetSmoothingSeconds(double seconds)
    {
        if (Volatile.Read(ref _smoothingSeconds) == seconds) return;
        Volatile.Write(ref _smoothingSeconds, seconds);
        try { SmoothingChanged?.Invoke(seconds); }
        catch (Exception) { /* isolated like StateChanged */ }
    }

    void PlayoutLoop(Playout playout, nint decoder, nint frame, nint sw)
    {
        var dec = (AVCodecContext*)decoder;
        var clock = new PlayoutClock(InitialSmoothingSeconds, MaxSmoothingSeconds);
        TimerResolution.Begin();
        try
        {
            while (!playout.Stop)
            {
                if (playout.FlushRequested)
                {
                    while (playout.Queue.TryDequeue(out var dropped)) Free(dropped);
                    playout.FlushRequested = false;
                }
                var now = Seconds(Stopwatch.GetTimestamp());
                var newest = Volatile.Read(ref playout.Newest);
                if (!playout.Queue.TryPeek(out var head))
                {
                    if (!double.IsNaN(newest))
                    {
                        var wasBuffering = clock.Buffering;
                        clock.Idle(newest, now);
                        if (clock.Buffering && !wasBuffering) Interlocked.Increment(ref _rebuffers);
                        SetSmoothingSeconds(clock.TargetDelay);
                    }
                    playout.Signal.WaitOne(20);
                    continue;
                }

                var present = true;
                if (!double.IsNaN(head.Key) && !double.IsNaN(newest))
                {
                    var decision = clock.Next(head.Key, newest, now);
                    SetSmoothingSeconds(clock.TargetDelay);
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
            TimerResolution.End();
        }
    }

    /// <summary>1 ms system timer while a smoothed view paces frames (the default 15.6 ms makes frames uneven).</summary>
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
