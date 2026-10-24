using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public unsafe class FrameConverterTests
{
    // Pure red in BT.709, limited range.
    const byte RedY = 63, RedU = 102, RedV = 240;

    /// <summary>A flat NV12 image of one colour with the given tags; the caller frees it.</summary>
    internal static AVFrame* Nv12(int width, int height, byte y, byte u, byte v,
        AVColorSpace space = AVColorSpace.AVCOL_SPC_UNSPECIFIED, AVColorRange range = AVColorRange.AVCOL_RANGE_UNSPECIFIED)
    {
        FFmpegLoader.Initialize();
        var frame = ffmpeg.av_frame_alloc();
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        frame->width = width;
        frame->height = height;
        frame->colorspace = space;
        frame->color_range = range;
        FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(frame, 0), "alloc");
        for (var row = 0; row < height; row++)
            new Span<byte>(frame->data[0] + row * frame->linesize[0], width).Fill(y);
        for (var row = 0; row < (height + 1) / 2; row++)
        {
            var uv = new Span<byte>(frame->data[1] + row * frame->linesize[1], (width + 1) / 2 * 2);
            for (var i = 0; i < uv.Length; i += 2) (uv[i], uv[i + 1]) = (u, v);
        }
        return frame;
    }

    static VideoFrame ToBgra(AVFrame* src, int width, int height)
    {
        var target = new VideoFrame(width, height);
        using var converter = new FrameConverter();
        converter.Convert(src, target);
        return target;
    }

    static void AssertPureRed(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            Assert.InRange(bgra[i], 0, 5);         // B
            Assert.InRange(bgra[i + 1], 0, 5);     // G
            Assert.InRange(bgra[i + 2], 250, 255); // R
        }
    }

    [Fact]
    public void Bt709_red_becomes_pure_red_in_every_pixel()
    {
        var src = Nv12(64, 64, RedY, RedU, RedV, AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG);
        try { AssertPureRed(ToBgra(src, 32, 32).Data); }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Untagged_hd_image_is_read_as_bt709()
    {
        var src = Nv12(1280, 720, RedY, RedU, RedV);
        try { AssertPureRed(ToBgra(src, 640, 360).Data); }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Untagged_sd_image_is_read_as_bt601()
    {
        var src = Nv12(640, 480, RedY, RedU, RedV);
        try
        {
            var bgra = ToBgra(src, 320, 240).Data;
            Assert.InRange(bgra[2], 225, 240); // BT.601 reads this sample as a darker red (≈233)
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Conversion_writes_the_inferred_tags_into_the_source()
    {
        var src = Nv12(1280, 720, RedY, RedU, RedV);
        try
        {
            ToBgra(src, 640, 360);
            Assert.Equal(AVColorSpace.AVCOL_SPC_BT709, src->colorspace);
            Assert.Equal(AVColorRange.AVCOL_RANGE_MPEG, src->color_range);
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Keeps_converting_when_the_filter_changes()
    {
        var src = Nv12(1920, 1080, RedY, RedU, RedV);
        try
        {
            using var converter = new FrameConverter();
            var big = new VideoFrame(1280, 720);
            converter.Convert(src, big);   // bicubic
            var small = new VideoFrame(320, 180);
            converter.Convert(src, small); // bilinear: a new context
            AssertPureRed(big.Data);
            AssertPureRed(small.Data);
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Fact]
    public void Interlaced_image_converts_too()
    {
        var src = Nv12(1920, 1080, RedY, RedU, RedV);
        try
        {
            src->flags |= ffmpeg.AV_FRAME_FLAG_INTERLACED;
            AssertPureRed(ToBgra(src, 1280, 720).Data);
        }
        finally { ffmpeg.av_frame_free(&src); }
    }

    [Theory]
    [InlineData(1920, SwsFlags.SWS_BICUBIC)]
    [InlineData(960, SwsFlags.SWS_BICUBIC)]
    [InlineData(959, SwsFlags.SWS_BILINEAR)]
    [InlineData(320, SwsFlags.SWS_BILINEAR)]
    public void Large_images_use_bicubic(int width, SwsFlags expected) =>
        Assert.Equal(expected, FrameConverter.FilterFor(width));

    [Fact]
    public void Nv12_output_keeps_the_samples_and_says_how_to_read_them()
    {
        var src = Nv12(1280, 720, RedY, RedU, RedV);
        try
        {
            var target = new VideoFrame(640, 360, FrameLayout.Nv12);
            using var converter = new FrameConverter();
            converter.Convert(src, target);
            Assert.Equal(RedY, target.Data[0]);
            Assert.Equal(RedY, target.Data[target.ChromaOffset - 1]);
            Assert.Equal(RedU, target.Data[target.ChromaOffset]);
            Assert.Equal(RedV, target.Data[target.ChromaOffset + 1]);
            Assert.Equal(RedV, target.Data[^1]);
            Assert.Equal((YuvMatrix.Bt709, false), (target.Matrix, target.FullRange));
        }
        finally { ffmpeg.av_frame_free(&src); }
    }
}
