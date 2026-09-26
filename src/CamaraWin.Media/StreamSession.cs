using FFmpeg.AutoGen;

namespace CamaraWin.Media;

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

    public StreamSession(string url, bool useUdp = false, bool decode = true)
    {
        _url = url;
        _useUdp = useUdp;
        _decode = decode;
        // Delegates are kept in fields so the GC never collects them while FFmpeg holds the pointer.
        _interrupt = _ => _stopping || Environment.TickCount64 > Interlocked.Read(ref _deadline) ? 1 : 0;
        _getFormat = GetFormat;
    }

    public FrameMailbox Mailbox { get; } = new();
    public SessionState State => _state;
    public string? LastError { get; private set; }
    public event Action<SessionState>? StateChanged;

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
                try
                {
                    PlayOnce(hwDevice, ref reachedPlaying);
                }
                catch (FFmpegException ex) when (ex.IsAuthError)
                {
                    LastError = ex.Message;
                    SetState(SessionState.AuthFailed);
                    return;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
                if (_stopping) break;
                if (reachedPlaying) backoff = 1;
                SetState(SessionState.Reconnecting);
                _stopSignal.Wait(TimeSpan.FromSeconds(backoff));
                backoff = Math.Min(backoff * 2, MaxBackoffSeconds);
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
            if (_decode) dec = OpenDecoder(codec, stream->codecpar, hwDevice);

            while (!_stopping)
            {
                ArmDeadline(StallTimeoutMs);
                FFmpegException.ThrowIfError(ffmpeg.av_read_frame(fmt, pkt), "read");
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
        LastError = null;
        SetState(SessionState.Playing);
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
