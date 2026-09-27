using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Decodes one connection's audio on its own thread so video never waits. Packets are cloned into a bounded queue
/// (oldest dropped); with no sink attached packets are not queued at all.
/// </summary>
sealed unsafe class AudioPump : IDisposable
{
    const int MaxQueuedPackets = 50;

    readonly Func<IAudioSink?> _sink;
    readonly Action<string> _failed;
    readonly Queue<nint> _queue = new();
    readonly object _lock = new();
    readonly AutoResetEvent _signal = new(false); // Set is idempotent: one pending wake-up at most
    readonly Thread _thread;
    AVCodecParameters* _parameters;
    long _framesDecoded;
    volatile bool _stopping;
    volatile bool _broken;

    public AudioPump(AVCodecParameters* source, Func<IAudioSink?> sink, Action<string> failed)
    {
        _sink = sink;
        _failed = failed;
        var parameters = ffmpeg.avcodec_parameters_alloc();
        if (parameters is null) throw new InvalidOperationException("avcodec_parameters_alloc failed");
        var err = ffmpeg.avcodec_parameters_copy(parameters, source);
        if (err < 0)
        {
            ffmpeg.avcodec_parameters_free(&parameters);
            FFmpegException.ThrowIfError(err, "audio parameters");
        }
        _parameters = parameters;
        _thread = new Thread(Run) { IsBackground = true, Name = "AudioPump" };
        _thread.Start();
    }

    /// <summary>Decoded audio frames so far; readable from any thread.</summary>
    public long FramesDecoded => Interlocked.Read(ref _framesDecoded);

    /// <summary>Session thread. Clones the packet; never blocks.</summary>
    public void Enqueue(AVPacket* packet)
    {
        if (_broken || _stopping) return;
        var clone = ffmpeg.av_packet_clone(packet);
        if (clone is null) return;
        lock (_lock)
        {
            if (_queue.Count >= MaxQueuedPackets)
            {
                var oldest = (AVPacket*)_queue.Dequeue();
                ffmpeg.av_packet_free(&oldest);
            }
            _queue.Enqueue((nint)clone);
        }
        _signal.Set();
    }

    void Run()
    {
        AVCodecContext* dec = null;
        SwrContext* swr = null;
        var frame = ffmpeg.av_frame_alloc();
        float[] buffer = [];
        (int Rate, int Format, int Channels) swrInput = default;
        try
        {
            if (frame is null) throw new InvalidOperationException("av_frame_alloc failed");
            var codec = ffmpeg.avcodec_find_decoder(_parameters->codec_id);
            if (codec is null) throw new InvalidOperationException($"no hay descodificador para {ffmpeg.avcodec_get_name(_parameters->codec_id)}");
            dec = ffmpeg.avcodec_alloc_context3(codec);
            if (dec is null) throw new InvalidOperationException("avcodec_alloc_context3 failed");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_parameters_to_context(dec, _parameters), "audio decoder parameters");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_open2(dec, codec, null), "open audio decoder");

            while (!_stopping)
            {
                _signal.WaitOne(200);
                while (!_stopping && TryDequeue(out var packet))
                {
                    try
                    {
                        if (_sink() is not { } sink) continue; // detached meanwhile: drop
                        if (ffmpeg.avcodec_send_packet(dec, packet) < 0) continue;
                        while (ffmpeg.avcodec_receive_frame(dec, frame) >= 0)
                        {
                            Interlocked.Increment(ref _framesDecoded);
                            var input = (frame->sample_rate, frame->format, frame->ch_layout.nb_channels);
                            if (swr is null || input != swrInput)
                            {
                                ffmpeg.swr_free(&swr);
                                swr = CreateResampler(frame);
                                swrInput = input;
                            }
                            var capacity = ffmpeg.swr_get_out_samples(swr, frame->nb_samples);
                            FFmpegException.ThrowIfError(capacity, "resampler size");
                            if (buffer.Length < capacity * AudioFormat.Channels) buffer = new float[capacity * AudioFormat.Channels];
                            int converted;
                            fixed (float* output = buffer)
                            {
                                var outputs = (byte*)output;
                                converted = ffmpeg.swr_convert(swr, &outputs, capacity, frame->extended_data, frame->nb_samples);
                            }
                            ffmpeg.av_frame_unref(frame);
                            FFmpegException.ThrowIfError(converted, "resample");
                            if (converted > 0) sink.Write(buffer.AsSpan(0, converted * AudioFormat.Channels));
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_free(&packet);
                    }
                }
            }
        }
        catch (Exception ex) when (!_stopping)
        {
            _broken = true;
            try { _failed(CredentialSanitizer.Sanitize(ex.Message)); }
            catch (Exception) { /* isolated */ }
        }
        catch (Exception)
        {
            // stopping
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.swr_free(&swr);
            ffmpeg.avcodec_free_context(&dec);
            DrainQueue();
        }
    }

    static SwrContext* CreateResampler(AVFrame* frame)
    {
        SwrContext* swr = null;
        AVChannelLayout outLayout = default;
        AVChannelLayout inLayout = default;
        ffmpeg.av_channel_layout_default(&outLayout, AudioFormat.Channels);
        try
        {
            // Streams described only by a channel count (unspecified order) get the standard layout for that count.
            if (frame->ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC)
                ffmpeg.av_channel_layout_default(&inLayout, frame->ch_layout.nb_channels);
            else
                FFmpegException.ThrowIfError(ffmpeg.av_channel_layout_copy(&inLayout, &frame->ch_layout), "audio layout");
            var err = ffmpeg.swr_alloc_set_opts2(&swr, &outLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, AudioFormat.SampleRate,
                &inLayout, (AVSampleFormat)frame->format, frame->sample_rate, 0, null);
            if (err >= 0) err = ffmpeg.swr_init(swr);
            if (err < 0)
            {
                ffmpeg.swr_free(&swr);
                FFmpegException.ThrowIfError(err, "resampler");
            }
            return swr;
        }
        finally
        {
            ffmpeg.av_channel_layout_uninit(&inLayout);
            ffmpeg.av_channel_layout_uninit(&outLayout);
        }
    }

    bool TryDequeue(out AVPacket* packet)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                packet = null;
                return false;
            }
            packet = (AVPacket*)_queue.Dequeue();
            return true;
        }
    }

    void DrainQueue()
    {
        while (TryDequeue(out var packet)) ffmpeg.av_packet_free(&packet);
    }

    /// <summary>Session thread. Stops the pump; the pump thread frees its own decoder and resampler.</summary>
    public void Dispose()
    {
        if (_stopping) return;
        _stopping = true;
        _signal.Set();
        var joined = _thread.Join(TimeSpan.FromSeconds(2));
        DrainQueue();
        // A pump thread still running may be reading the parameters: leak them rather than free them under it.
        if (!joined) return;
        var parameters = _parameters;
        ffmpeg.avcodec_parameters_free(&parameters);
        _parameters = null;
        _signal.Dispose();
    }
}
