using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Turns a decoded frame into a mailbox frame (BGRA, or NV12 for the graphics card) with swscale: with the frame's
/// real colours (<see cref="ColorTags"/>); large images bicubic and over several cores. One per session; only its
/// decoding thread calls it.
/// </summary>
sealed unsafe class FrameConverter : IDisposable
{
    /// <summary>Images at least this wide are scaled bicubic (sharper); smaller ones bilinear (cheaper).</summary>
    internal const int BicubicFromWidth = 960;

    /// <summary>
    /// Cores per large conversion: enough for a 4 MP image without one camera taking the whole processor. Small tiles
    /// get one: 16 of them must not mean 64 swscale threads.
    /// </summary>
    internal static int Threads { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    SwsContext* _sws;
    SwsFlags _filter;
    AVFrame* _dst;

    // Same threshold as the filter, whose change already recreates the context.
    internal static int ThreadsFor(int width) => width >= BicubicFromWidth ? Threads : 1;

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
            _sws->threads = ThreadsFor(target.Width);
            _sws->flags = (uint)filter;
            _filter = filter;
        }
        var format = target.Layout == FrameLayout.Nv12 ? AVPixelFormat.AV_PIX_FMT_NV12 : AVPixelFormat.AV_PIX_FMT_BGRA;
        if (_dst is null) _dst = ffmpeg.av_frame_alloc();
        // swscale writes past each row's width: it gets FFmpeg's padded buffer and the image is copied tight into the
        // managed array afterwards.
        if (_dst->buf[0] is null || _dst->width != target.Width || _dst->height != target.Height || _dst->format != (int)format)
        {
            ffmpeg.av_frame_unref(_dst);
            _dst->format = (int)format;
            _dst->width = target.Width;
            _dst->height = target.Height;
            FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(_dst, 0), "alloc frame");
        }
        // Same primaries and transfer as the source: only the matrix is converted, never the gamut.
        _dst->color_primaries = src->color_primaries;
        _dst->color_trc = src->color_trc;
        // Scaled as one picture, like v1.3: field by field combs at screen size. swscale wants both flags equal.
        src->flags &= ~ffmpeg.AV_FRAME_FLAG_INTERLACED;
        _dst->flags = 0;
        if (target.Layout == FrameLayout.Nv12)
        {
            // Scaled only, same matrix and range: the graphics card converts to RGB with them.
            _dst->colorspace = src->colorspace;
            _dst->color_range = src->color_range;
            target.Matrix = tags.Matrix;
            target.FullRange = tags.FullRange;
        }
        else
        {
            _dst->colorspace = AVColorSpace.AVCOL_SPC_RGB;
            _dst->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        }
        FFmpegException.ThrowIfError(ffmpeg.sws_scale_frame(_sws, _dst, src), "scale");
        var planes = new byte_ptrArray4();
        var linesizes = new int_array4();
        for (uint i = 0; i < 4; i++)
        {
            planes[i] = _dst->data[i];
            linesizes[i] = _dst->linesize[i];
        }
        int copied;
        fixed (byte* data = target.Data)
            copied = ffmpeg.av_image_copy_to_buffer(data, target.Data.Length, planes, linesizes, format, target.Width, target.Height, 1);
        FFmpegException.ThrowIfError(copied, "copy frame");
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
