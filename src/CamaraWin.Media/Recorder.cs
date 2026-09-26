using System.Collections.Concurrent;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

/// <summary>
/// Copies compressed packets into a Matroska file without re-encoding. Disk I/O runs on its own
/// thread behind a bounded queue so a slow disk can never stall the network reader.
/// </summary>
public sealed unsafe class Recorder
{
    const int QueueCapacity = 512;

    readonly BlockingCollection<nint> _queue = new(QueueCapacity);
    readonly AVFormatContext* _output;
    readonly AVRational _inputTimeBase;
    readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    long _startTimestamp = ffmpeg.AV_NOPTS_VALUE;

    public Recorder(string path, AVCodecParameters* input, AVRational inputTimeBase)
    {
        Path = path;
        _inputTimeBase = inputTimeBase;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        AVFormatContext* output = null;
        FFmpegException.ThrowIfError(ffmpeg.avformat_alloc_output_context2(&output, null, "matroska", path), "create mkv");
        try
        {
            var stream = ffmpeg.avformat_new_stream(output, null);
            FFmpegException.ThrowIfError(ffmpeg.avcodec_parameters_copy(stream->codecpar, input), "copy codec parameters");
            stream->codecpar->codec_tag = 0;
            stream->time_base = inputTimeBase;
            FFmpegException.ThrowIfError(ffmpeg.avio_open(&output->pb, path, ffmpeg.AVIO_FLAG_WRITE), "open file");
            FFmpegException.ThrowIfError(ffmpeg.avformat_write_header(output, null), "write header");
        }
        catch
        {
            if (output->pb is not null) ffmpeg.avio_closep(&output->pb);
            ffmpeg.avformat_free_context(output);
            throw;
        }
        _output = output;
        new Thread(WriteLoop) { IsBackground = true, Name = "Recorder" }.Start();
    }

    public string Path { get; }
    public string? Error { get; private set; }
    public Task Completion => _done.Task;

    /// <summary>Queues a copy of the packet. False means recording must stop (see <see cref="Error"/>).</summary>
    public bool Enqueue(AVPacket* packet)
    {
        if (Error is not null || _queue.IsAddingCompleted) return false;
        var clone = ffmpeg.av_packet_clone(packet);
        if (clone is null) return false;
        if (_queue.TryAdd((nint)clone)) return true;
        ffmpeg.av_packet_free(&clone);
        Error = "El disco no da abasto; grabación detenida.";
        return false;
    }

    /// <summary>Stops accepting packets; the writer drains the queue and finalizes the file.</summary>
    public void Complete()
    {
        if (!_queue.IsAddingCompleted) _queue.CompleteAdding();
    }

    void WriteLoop()
    {
        try
        {
            foreach (var handle in _queue.GetConsumingEnumerable())
            {
                var packet = (AVPacket*)handle;
                try
                {
                    if (Error is null) Write(packet);
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                }
            }
        }
        finally
        {
            ffmpeg.av_write_trailer(_output);
            var output = _output;
            ffmpeg.avio_closep(&output->pb);
            ffmpeg.avformat_free_context(output);
            _done.TrySetResult();
        }
    }

    void Write(AVPacket* packet)
    {
        var noTimestamp = ffmpeg.AV_NOPTS_VALUE;
        if (_startTimestamp == noTimestamp)
            _startTimestamp = packet->dts != noTimestamp ? packet->dts : packet->pts;
        if (packet->pts != noTimestamp) packet->pts -= _startTimestamp;
        if (packet->dts != noTimestamp) packet->dts -= _startTimestamp;
        packet->stream_index = 0;
        packet->pos = -1;
        ffmpeg.av_packet_rescale_ts(packet, _inputTimeBase, _output->streams[0]->time_base);

        var err = ffmpeg.av_interleaved_write_frame(_output, packet);
        if (err < 0) Error = $"Error al escribir: {FFmpegException.Describe(err)}";
    }
}
