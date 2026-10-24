using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Turns a decoded frame into a mailbox frame (BGRA, or NV12 for the graphics card) with swscale: with the frame's
/// real colours (<see cref="ColorTags"/>), over several cores, bicubic for large images. One per session; only its
/// decoding thread calls it.
/// </summary>
sealed unsafe class FrameConverter : IDisposable
{
    /// <summary>Images at least this wide are scaled bicubic (sharper); smaller ones bilinear (cheaper).</summary>
    internal const int BicubicFromWidth = 960;

    /// <summary>Cores per conversion: enough for a 4 MP image without one camera taking the whole processor.</summary>
    internal static int Threads { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    SwsContext* _sws;
    SwsFlags _filter;
    AVFrame* _dst;

    internal static SwsFlags FilterFor(int width) =>
        width >= BicubicFromWidth ? SwsFlags.SWS_BICUBIC : SwsFlags.SWS_BILINEAR;

    /// <summary>Converts <paramref name="src"/> into <paramref name="target"/>, scaled to its size; tags <paramref name="src"/> with its inferred colours.</summary>
    public void Convert(AVFrame* src, VideoFrame target)
    {
        var tags = ColorTags.Apply(src);
        var filter = FilterFor(target.Width);
        if (_sws is null || filter != _filter)
        {
            FreeContext();
            _sws = ffmpeg.sws_alloc_context();
            if (_sws is null) throw new InvalidOperationException("sws_alloc_context failed");
            _sws->threads = Threads;
            _sws->flags = (uint)filter;
            _filter = filter;
        }
        if (_dst is null) _dst = ffmpeg.av_frame_alloc();
        _dst->width = target.Width;
        _dst->height = target.Height;
        // Same primaries and transfer as the source: only the matrix is converted, never the gamut.
        _dst->color_primaries = src->color_primaries;
        _dst->color_trc = src->color_trc;
        // swscale refuses source and destination with different interlacing; matching it scales each field apart.
        _dst->flags = src->flags & ffmpeg.AV_FRAME_FLAG_INTERLACED;
        if (target.Layout == FrameLayout.Nv12)
        {
            // Scaled only, same matrix and range: the graphics card converts to RGB with them.
            _dst->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
            _dst->colorspace = src->colorspace;
            _dst->color_range = src->color_range;
            target.Matrix = tags.Matrix;
            target.FullRange = tags.FullRange;
        }
        else
        {
            _dst->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            _dst->colorspace = AVColorSpace.AVCOL_SPC_RGB;
            _dst->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        }
        int err;
        fixed (byte* data = target.Data)
        {
            _dst->data[0] = data;
            _dst->linesize[0] = target.Stride;
            if (target.Layout == FrameLayout.Nv12)
            {
                _dst->data[1] = data + target.ChromaOffset;
                _dst->linesize[1] = target.ChromaWidth * 2;
            }
            err = ffmpeg.sws_scale_frame(_sws, _dst, src);
            _dst->data[0] = null; // managed buffer: never left behind in the frame
            _dst->data[1] = null;
            _dst->linesize[1] = 0;
        }
        FFmpegException.ThrowIfError(err, "scale");
    }

    public void Dispose()
    {
        FreeContext();
        if (_dst is null) return;
        var dst = _dst;
        ffmpeg.av_frame_free(&dst);
        _dst = null;
    }

    void FreeContext()
    {
        if (_sws is null) return;
        var sws = _sws;
        ffmpeg.sws_free_context(&sws);
        _sws = null;
    }
}
