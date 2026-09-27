using System.Diagnostics;
using Centinela.Core;
using FFmpeg.AutoGen;

namespace Centinela.Media;

public enum SessionState { Idle, Connecting, Playing, Reconnecting, AuthFailed, Stopped }

/// <summary>
/// One RTSP connection on its own thread. Decoding sessions push the newest frame (BGRA, scaled to
/// the target size) into <see cref="Mailbox"/>; there is no buffering, clock or queue on purpose.
/// </summary>
public sealed unsafe partial class StreamSession : IDisposable
{
    const int OpenTimeoutMs = 10_000;
    const int StallTimeoutMs = 5_000;
    const int MaxBackoffSeconds = 10;
    const int MaxConsecutiveDecodeErrors = 100;
    const int SlowRetrySeconds = 30;

    readonly string _url;
    readonly bool _useUdp;
    readonly bool _decode;
    readonly AVIOInterruptCB_callback _interrupt;
    readonly AVCodecContext_get_format _getFormat;
    readonly ManualResetEventSlim _stopSignal = new(false);
    readonly object _snapshotLock = new();

    Thread? _thread;
    volatile bool _stopping;
    volatile SessionState _state = SessionState.Idle;
    long _deadline;
    volatile int _targetWidth;
    volatile int _targetHeight;
    bool _forceSoftware;
    int _decodeErrors;
    SwsContext* _sws;
    AVFrame* _lastFrame; // native-resolution software frame for snapshots; guarded by _snapshotLock
    volatile bool _deadlineHit;
    volatile int _lastRtspStatus;          // 0 = none, for the current attempt
    volatile string? _lastLogLine;
    volatile StreamError? _lastError;      // LastError and LastErrorKind derive from this one reference
    StreamStats _stats = StreamStats.Empty;
    long _lastFrameTimestamp;       // Stopwatch ticks, 0 = none
    long _statsWindowStart;
    int _statsFrames;
    double _fps;
    double _latencyEma = -1;
    long _packetTimestamp;          // when the packet being decoded was read
    volatile StreamInfo? _info;     // null until known for the current connection
    bool _infoPublished;            // session thread only
    int _audioIndex = -1;           // current connection's audio stream, -1 = none
    string? _videoCodecName;
    string? _audioCodecName;

    public StreamSession(string url, bool useUdp = false, bool decode = true)
    {
        _url = url;
        _useUdp = useUdp;
        _decode = decode;
        // Delegates are kept in fields so the GC never collects them while FFmpeg holds the pointer.
        _interrupt = _ =>
        {
            if (_stopping) return 1;
            if (Environment.TickCount64 <= Interlocked.Read(ref _deadline)) return 0;
            _deadlineHit = true;
            return 1;
        };
        _getFormat = GetFormat;
    }

    public FrameMailbox Mailbox { get; } = new();
    public SessionState State => _state;
    public string? LastError => _lastError?.Detail;
    public StreamErrorKind? LastErrorKind => _lastError?.Kind;
    public event Action<SessionState>? StateChanged;
    /// <summary>Raised on the session thread for every failed attempt or stall; subscriber exceptions are isolated.</summary>
    public event Action<StreamError>? ErrorOccurred;
    /// <summary>Resolution and codecs of the current connection; null until known.</summary>
    public StreamInfo? Info => _info;
    /// <summary>Raised on the session thread once per connection when <see cref="Info"/> is known; subscriber exceptions are isolated.</summary>
    public event Action<StreamInfo>? InfoAvailable;

    public StreamStats Stats
    {
        get
        {
            var stats = Volatile.Read(ref _stats);
            var last = Interlocked.Read(ref _lastFrameTimestamp);
            if (last == 0) return stats;
            var since = Stopwatch.GetElapsedTime(last);
            // No frame for over a second: the last measured rate no longer describes the stream.
            return since > TimeSpan.FromSeconds(1) ? stats with { Fps = 0, SinceLastFrame = since } : stats with { SinceLastFrame = since };
        }
    }

    /// <summary>Box the decoded image must fit in, in device pixels. 0 = native size.</summary>
    public void SetTargetSize(int width, int height)
    {
        _targetWidth = Math.Max(0, width);
        _targetHeight = Math.Max(0, height);
    }

    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("Session already started.");
        _thread = new Thread(Run) { IsBackground = true, Name = "StreamSession" };
        _thread.Start();
    }

    public void RequestStop()
    {
        _stopping = true;
        _stopSignal.Set();
    }

    public void Stop()
    {
        RequestStop();
        if (_thread is { } thread && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(3));
            WaitForRecorders(); // recorder threads are background threads: let them write the trailer
        }
    }

    public void Dispose() => Stop();

    void Run()
    {
        AVBufferRef* hwDevice = null;
        if (_decode && ffmpeg.av_hwdevice_ctx_create(&hwDevice, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) < 0)
            hwDevice = null;
        var backoff = 1;
        try
        {
            SetState(SessionState.Connecting);
            while (!_stopping)
            {
                var reachedPlaying = false;
                StreamError? error = null;
                try
                {
                    PlayOnce(hwDevice, ref reachedPlaying);
                }
                catch (Exception ex) when (!_stopping)
                {
                    error = BuildError(ex, reachedPlaying);
                }
                catch (Exception)
                {
                    // stopping: interruption is expected
                }
                if (_stopping) break;
                if (error is not null)
                {
                    Report(error);
                    if (error.Kind == StreamErrorKind.AuthFailed)
                    {
                        SetState(SessionState.AuthFailed);
                        return;
                    }
                }
                if (reachedPlaying) backoff = 1;
                SetState(SessionState.Reconnecting);
                var wait = error?.Kind is StreamErrorKind.NotFound or StreamErrorKind.CameraBusy ? SlowRetrySeconds : backoff;
                _stopSignal.Wait(TimeSpan.FromSeconds(wait));
                if (wait == backoff) backoff = Math.Min(backoff * 2, MaxBackoffSeconds);
            }
        }
        finally
        {
            OnSessionEnding();
            lock (_snapshotLock)
            {
                var last = _lastFrame;
                ffmpeg.av_frame_free(&last);
                _lastFrame = null;
            }
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
            ffmpeg.av_buffer_unref(&hwDevice);
            if (_state != SessionState.AuthFailed) SetState(SessionState.Stopped);
        }
    }

    void PlayOnce(AVBufferRef* hwDevice, ref bool reachedPlaying)
    {
        var fmt = ffmpeg.avformat_alloc_context();
        var logContext = (nint)fmt; // FFmpeg frees fmt on a failed open; unregister by the original address
        _deadlineHit = false;
        _lastRtspStatus = 0;
        _lastLogLine = null;
        _statsFrames = 0;
        _statsWindowStart = 0;
        _latencyEma = -1;
        _fps = 0;
        _info = null;
        _infoPublished = false;
        _audioIndex = -1;
        _videoCodecName = null;
        _audioCodecName = null;
        var logSink = (Action<string>)OnFFmpegLog; // identity for Unregister: fmt's address may be reused by another session
        FFmpegLog.Register(fmt, logSink);
        AVCodecContext* dec = null;
        var pkt = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();
        var sw = ffmpeg.av_frame_alloc();
        try
        {
            fmt->interrupt_callback.callback = _interrupt;
            ArmDeadline(OpenTimeoutMs);

            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "rtsp_transport", _useUdp ? "udp" : "tcp", 0);
            ffmpeg.av_dict_set(&options, "fflags", "nobuffer", 0);
            ffmpeg.av_dict_set(&options, "probesize", "32768", 0);
            ffmpeg.av_dict_set(&options, "max_delay", "0", 0);
            ffmpeg.av_dict_set(&options, "reorder_queue_size", "0", 0);
            ffmpeg.av_dict_set(&options, "timeout", "5000000", 0);
            var err = ffmpeg.avformat_open_input(&fmt, _url, null, &options);
            ffmpeg.av_dict_free(&options);
            FFmpegException.ThrowIfError(err, "open"); // on failure FFmpeg already freed fmt and set it to null

            // Live view skips avformat_find_stream_info: it waits for frames and adds seconds of latency.
            // Recording needs width/height for the MKV header, and latency does not matter there.
            if (!_decode) FFmpegException.ThrowIfError(ffmpeg.avformat_find_stream_info(fmt, null), "stream info");

            // Remux-only sessions skip the decoder lookup so codecs we cannot decode can still be recorded.
            AVCodec* codec = null;
            var videoIndex = FFmpegException.ThrowIfError(
                _decode
                    ? ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0)
                    : ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0),
                "find video");
            var stream = fmt->streams[videoIndex];
            var audio = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, videoIndex, null, 0);
            _audioIndex = audio >= 0 ? audio : -1;
            _videoCodecName = ffmpeg.avcodec_get_name(stream->codecpar->codec_id);
            _audioCodecName = _audioIndex >= 0 ? ffmpeg.avcodec_get_name(fmt->streams[_audioIndex]->codecpar->codec_id) : null;
            // Remux sessions ran avformat_find_stream_info, so the size is known now; decode sessions wait for a frame.
            if (!_decode && stream->codecpar->width > 0) PublishInfo(stream->codecpar->width, stream->codecpar->height);
            if (_decode) dec = OpenDecoder(codec, stream->codecpar, hwDevice);

            while (!_stopping)
            {
                ArmDeadline(StallTimeoutMs);
                FFmpegException.ThrowIfError(ffmpeg.av_read_frame(fmt, pkt), "read");
                _packetTimestamp = Stopwatch.GetTimestamp();
                if (pkt->stream_index == videoIndex)
                {
                    OnVideoPacket(stream, pkt);
                    if (dec is null) MarkPlaying(ref reachedPlaying);
                    else DecodePacket(dec, pkt, frame, sw, ref reachedPlaying);
                }
                ffmpeg.av_packet_unref(pkt);
            }
        }
        finally
        {
            FFmpegLog.Unregister((void*)logContext, logSink);
            OnConnectionClosed();
            ffmpeg.av_frame_free(&sw);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&pkt);
            ffmpeg.avcodec_free_context(&dec);
            ffmpeg.avformat_close_input(&fmt);
        }
    }

    AVCodecContext* OpenDecoder(AVCodec* codec, AVCodecParameters* parameters, AVBufferRef* hwDevice)
    {
        var dec = ffmpeg.avcodec_alloc_context3(codec);
        var err = ffmpeg.avcodec_parameters_to_context(dec, parameters);
        if (err >= 0)
        {
            dec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            dec->thread_type = ffmpeg.FF_THREAD_SLICE; // frame threading adds a frame of delay per thread
            dec->thread_count = 2;
            if (hwDevice is not null && !_forceSoftware)
            {
                dec->hw_device_ctx = ffmpeg.av_buffer_ref(hwDevice);
                dec->get_format = _getFormat;
            }
            err = ffmpeg.avcodec_open2(dec, codec, null);
        }
        if (err < 0)
        {
            ffmpeg.avcodec_free_context(&dec);
            FFmpegException.ThrowIfError(err, "open decoder");
        }
        return dec;
    }

    static AVPixelFormat GetFormat(AVCodecContext* context, AVPixelFormat* formats)
    {
        for (var p = formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
            if (*p == AVPixelFormat.AV_PIX_FMT_D3D11) return *p;
        return ffmpeg.avcodec_default_get_format(context, formats);
    }

    void DecodePacket(AVCodecContext* dec, AVPacket* pkt, AVFrame* frame, AVFrame* sw, ref bool reachedPlaying)
    {
        var again = ffmpeg.AVERROR(ffmpeg.EAGAIN);
        var err = ffmpeg.avcodec_send_packet(dec, pkt);
        if (err < 0 && err != again)
        {
            CountDecodeError(err);
            return;
        }
        while (true)
        {
            err = ffmpeg.avcodec_receive_frame(dec, frame);
            if (err == again || err == ffmpeg.AVERROR_EOF) return;
            if (err < 0)
            {
                CountDecodeError(err);
                return;
            }
            _decodeErrors = 0;
            PresentFrame(frame, sw);
            ffmpeg.av_frame_unref(frame);
            MarkPlaying(ref reachedPlaying);
        }
    }

    void CountDecodeError(int err)
    {
        if (++_decodeErrors < MaxConsecutiveDecodeErrors) return;
        _decodeErrors = 0;
        _forceSoftware = true; // persistent failures: reconnect and decode on the CPU
        throw new FFmpegException(err, "decode");
    }

    void PresentFrame(AVFrame* frame, AVFrame* sw)
    {
        var hardware = frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11;
        var src = frame;
        if (frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11)
        {
            ffmpeg.av_frame_unref(sw);
            var err = ffmpeg.av_hwframe_transfer_data(sw, frame, 0);
            if (err < 0)
            {
                _forceSoftware = true;
                FFmpegException.ThrowIfError(err, "gpu transfer");
            }
            src = sw;
        }
        KeepForSnapshot(src);
        if (!_infoPublished) PublishInfo(src->width, src->height);

        var (width, height) = FrameGeometry.FitSize(src->width, src->height, _targetWidth, _targetHeight);
        var target = Mailbox.Rent(width, height);
        _sws = ffmpeg.sws_getCachedContext(_sws, src->width, src->height, (AVPixelFormat)src->format,
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_sws is null) throw new InvalidOperationException("sws_getCachedContext failed");

        fixed (byte* dst = target.Data)
        {
            ffmpeg.sws_scale(_sws, src->data.ToArray(), src->linesize.ToArray(), 0, src->height,
                new byte*[] { dst, null, null, null }, new[] { target.Stride, 0, 0, 0 });
        }
        Mailbox.Publish(target);
        UpdateStats(hardware);
    }

    void UpdateStats(bool hardware)
    {
        var now = Stopwatch.GetTimestamp();
        var latency = Stopwatch.GetElapsedTime(_packetTimestamp, now).TotalMilliseconds;
        _latencyEma = _latencyEma < 0 ? latency : _latencyEma * 0.9 + latency * 0.1;
        _statsFrames++;
        if (_statsWindowStart == 0) _statsWindowStart = now;
        var window = Stopwatch.GetElapsedTime(_statsWindowStart, now).TotalSeconds;
        if (window >= 1)
        {
            _fps = _statsFrames / window;
            _statsFrames = 0;
            _statsWindowStart = now;
        }
        Interlocked.Exchange(ref _lastFrameTimestamp, now);
        Volatile.Write(ref _stats, new StreamStats(_fps, _latencyEma, hardware, TimeSpan.Zero));
    }

    void KeepForSnapshot(AVFrame* src)
    {
        lock (_snapshotLock)
        {
            if (_lastFrame is null) _lastFrame = ffmpeg.av_frame_alloc();
            else ffmpeg.av_frame_unref(_lastFrame);
            ffmpeg.av_frame_ref(_lastFrame, src);
        }
    }

    void MarkPlaying(ref bool reachedPlaying)
    {
        reachedPlaying = true;
        if (_state == SessionState.Playing) return;
        _lastError = null;
        // Lines logged before playback (non-fatal warnings) must not reclassify a later stall.
        _lastRtspStatus = 0;
        _lastLogLine = null;
        SetState(SessionState.Playing);
    }

    StreamError BuildError(Exception ex, bool wasPlaying)
    {
        var code = ex is FFmpegException f ? f.ErrorCode : 0;
        int? status = _lastRtspStatus == 0 ? null : _lastRtspStatus;
        var kind = StreamErrorClassifier.Classify(code, status, wasPlaying, _deadlineHit);
        var detail = _lastLogLine is { } line ? $"{ex.Message} — {line}" : ex.Message;
        return new StreamError(kind, status, code, CredentialSanitizer.Sanitize(detail), DateTime.UtcNow);
    }

    void Report(StreamError error)
    {
        _lastError = error;
        try { ErrorOccurred?.Invoke(error); }
        catch (Exception) { /* isolated like StateChanged */ }
    }

    void PublishInfo(int width, int height)
    {
        _infoPublished = true;
        var info = new StreamInfo(width, height, _videoCodecName ?? "?", _audioCodecName);
        _info = info;
        try { InfoAvailable?.Invoke(info); }
        catch (Exception) { /* isolated like StateChanged */ }
    }

    void OnFFmpegLog(string line)
    {
        _lastLogLine = line;
        if (RtspStatusParser.Parse(line) is { } status) _lastRtspStatus = status;
    }

    void ArmDeadline(int milliseconds) =>
        Interlocked.Exchange(ref _deadline, Environment.TickCount64 + milliseconds);

    void SetState(SessionState state)
    {
        if (_state == state) return;
        _state = state;
        try
        {
            StateChanged?.Invoke(state);
        }
        catch (Exception)
        {
            // A faulty subscriber must neither tear down a healthy stream nor crash this thread.
        }
    }

    // Recording hooks, implemented in StreamSession.Recording.cs.
    partial void OnVideoPacketCore(AVStream* stream, AVPacket* pkt);
    partial void OnConnectionClosedCore();
    partial void OnSessionEndingCore();
    void OnVideoPacket(AVStream* stream, AVPacket* pkt) => OnVideoPacketCore(stream, pkt);
    void OnConnectionClosed() => OnConnectionClosedCore();
    void OnSessionEnding() => OnSessionEndingCore();
}
