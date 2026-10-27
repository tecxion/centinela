using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public class ColorTagsTests
{
    const AVColorSpace UnsetSpace = AVColorSpace.AVCOL_SPC_UNSPECIFIED;
    const AVColorRange UnsetRange = AVColorRange.AVCOL_RANGE_UNSPECIFIED;

    [Theory]
    [InlineData(2160, YuvMatrix.Bt709)]
    [InlineData(1080, YuvMatrix.Bt709)]
    [InlineData(720, YuvMatrix.Bt709)]
    [InlineData(576, YuvMatrix.Bt601)]
    [InlineData(360, YuvMatrix.Bt601)]
    public void Unset_matrix_follows_the_height(int height, YuvMatrix expected) =>
        Assert.Equal(expected, ColorTags.Infer(UnsetSpace, UnsetRange, AVPixelFormat.AV_PIX_FMT_YUV420P, height).Matrix);

    [Theory]
    [InlineData(AVColorSpace.AVCOL_SPC_BT470BG, 1080, YuvMatrix.Bt601)]
    [InlineData(AVColorSpace.AVCOL_SPC_SMPTE170M, 1080, YuvMatrix.Bt601)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT709, 360, YuvMatrix.Bt709)]
    [InlineData(AVColorSpace.AVCOL_SPC_SMPTE240M, 360, YuvMatrix.Bt709)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT2020_NCL, 2160, YuvMatrix.Bt2020)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT2020_CL, 2160, YuvMatrix.Bt2020)]
    public void Tagged_matrix_wins_over_the_height(AVColorSpace space, int height, YuvMatrix expected) =>
        Assert.Equal(expected, ColorTags.Infer(space, UnsetRange, AVPixelFormat.AV_PIX_FMT_YUV420P, height).Matrix);

    [Theory]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUV420P, false)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_NV12, false)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUVJ420P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUVJ422P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_UNSPECIFIED, AVPixelFormat.AV_PIX_FMT_YUVJ440P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_JPEG, AVPixelFormat.AV_PIX_FMT_YUV420P, true)]
    [InlineData(AVColorRange.AVCOL_RANGE_MPEG, AVPixelFormat.AV_PIX_FMT_YUVJ420P, false)]
    public void Range_is_the_tag_or_full_for_jpeg_formats(AVColorRange range, AVPixelFormat format, bool full) =>
        Assert.Equal(full, ColorTags.Infer(UnsetSpace, range, format, 1080).FullRange);
}
