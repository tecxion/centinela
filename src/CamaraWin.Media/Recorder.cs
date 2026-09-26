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
    volatile string? _error;

    /// <summary>Creates the file; if <paramref name="path"/> exists, "_1", "_2", ... is appended instead of overwriting.</summary>
    public Recorder(string path, AVCodecParameters* input, AVRational inputTimeBase)
    {
        _inputTimeBase = inputTimeBase;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        path = ReserveUniquePath(path);
        Path = path;

        AVFormatContext* output = null;
        try
        {
            FFmpegException.ThrowIfError(ffmpeg.avformat_alloc_output_context2(&output, null, "matroska", path), "create mkv");
            var stream = ffmpeg.avformat_new_stream(output, null);
            if (stream is null) throw new InvalidOperationException("avformat_new_stream failed");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_parameters_copy(stream->codecpar, input), "copy codec parameters");
            stream->codecpar->codec_tag = 0;
            stream->time_base = inputTimeBase;
            FFmpegException.ThrowIfError(ffmpeg.avio_open(&output->pb, path, ffmpeg.AVIO_FLAG_WRITE), "open file");
            FFmpegException.ThrowIfError(ffmpeg.avformat_write_header(output, null), "write header");
        }
        catch
        {
            if (output is not null)
            {
                if (output->pb is not null) ffmpeg.avio_closep(&output->pb);
                ffmpeg.avformat_free_context(output);
            }
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        _output = output;
        new Thread(WriteLoop) { IsBackground = true, Name = "Recorder" }.Start();
    }

    public string Path { get; }
    /// <summary>First write or finalization failure; set before <see cref="Completion"/> finishes.</summary>
    public string? Error => _error;
    public Task Completion => _done.Task;

    /// <summary>Queues a copy of the packet. False means recording must stop (see <see cref="Error"/>).</summary>
    public bool Enqueue(AVPacket* packet)
    {
        if (_error is not null || _queue.IsAddingCompleted) return false;
        var clone = ffmpeg.av_packet_clone(packet);
        if (clone is null) return false;
        if (_queue.TryAdd((nint)clone)) return true;
        ffmpeg.av_packet_free(&clone);
        _error = "El disco no da abasto; grabación detenida.";
        return false;
    }

    /// <summary>Stops accepting packets; the writer drains the queue and finalizes the file. Idempotent.</summary>
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
                    if (_error is null) Write(packet);
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                }
            }
        }
        finally
        {
            var output = _output;
            var err = ffmpeg.av_write_trailer(output);
            if (err < 0) _error ??= $"Error al finalizar la grabación: {FFmpegException.Describe(err)}";
            err = ffmpeg.avio_closep(&output->pb);
            if (err < 0) _error ??= $"Error al cerrar el archivo: {FFmpegException.Describe(err)}";
            ffmpeg.avformat_free_context(output);
            _done.TrySetResult();
        }
    }

    void Write(AVPacket* packet)
    {
        var noTimestamp = ffmpeg.AV_NOPTS_VALUE;
        if (_startTimestamp == noTimestamp)
        {
            var first = packet->dts != noTimestamp ? packet->dts : packet->pts;
            if (first == noTimestamp) return; // cannot anchor the timeline on a packet without timestamps
            _startTimestamp = first;
        }
        if (packet->pts != noTimestamp) packet->pts -= _startTimestamp;
        if (packet->dts != noTimestamp) packet->dts -= _startTimestamp;
        packet->stream_index = 0;
        packet->pos = -1;
        ffmpeg.av_packet_rescale_ts(packet, _inputTimeBase, _output->streams[0]->time_base);

        var err = ffmpeg.av_interleaved_write_frame(_output, packet);
        if (err < 0) _error ??= $"Error al escribir: {FFmpegException.Describe(err)}";
    }

    /// <summary>Atomically creates an empty file at the first free name (clip.mkv, clip_1.mkv, ...).</summary>
    static string ReserveUniquePath(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path)!;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var extension = System.IO.Path.GetExtension(path);
        for (var i = 0; ; i++)
        {
            var candidate = i == 0 ? path : System.IO.Path.Combine(dir, $"{name}_{i}{extension}");
            if (Directory.Exists(candidate)) continue;
            try
            {
                new FileStream(candidate, FileMode.CreateNew, FileAccess.Write).Dispose();
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // taken; try the next suffix
            }
        }
    }
}
