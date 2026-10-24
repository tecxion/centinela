using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// The colour matrix and range a decoded frame really uses. Cameras often leave them unset; FFmpeg then assumes
/// BT.601, which tints HD images, so an unset matrix follows the height as players do (720 lines or more: BT.709).
/// </summary>
public static class ColorTags
{
    public static (YuvMatrix Matrix, bool FullRange) Infer(AVColorSpace space, AVColorRange range, AVPixelFormat format, int height)
    {
        var matrix = space switch
        {
            // SMPTE 240M (BT.709's HD predecessor) is closest to BT.709; a tagged matrix never falls to the height guess.
            AVColorSpace.AVCOL_SPC_BT709 or AVColorSpace.AVCOL_SPC_SMPTE240M => YuvMatrix.Bt709,
            AVColorSpace.AVCOL_SPC_BT2020_NCL or AVColorSpace.AVCOL_SPC_BT2020_CL => YuvMatrix.Bt2020,
            AVColorSpace.AVCOL_SPC_BT470BG or AVColorSpace.AVCOL_SPC_SMPTE170M or AVColorSpace.AVCOL_SPC_FCC => YuvMatrix.Bt601,
            _ => height >= 720 ? YuvMatrix.Bt709 : YuvMatrix.Bt601,
        };
        var fullRange = range switch
        {
            AVColorRange.AVCOL_RANGE_JPEG => true,
            AVColorRange.AVCOL_RANGE_MPEG => false,
            // The deprecated "J" formats (MJPEG) are full range by definition.
            _ => format is AVPixelFormat.AV_PIX_FMT_YUVJ420P or AVPixelFormat.AV_PIX_FMT_YUVJ422P
                or AVPixelFormat.AV_PIX_FMT_YUVJ444P or AVPixelFormat.AV_PIX_FMT_YUVJ440P or AVPixelFormat.AV_PIX_FMT_YUVJ411P,
        };
        return (matrix, fullRange);
    }

    /// <summary>Writes the inferred matrix and range into the frame, so swscale converts with them.</summary>
    public static unsafe (YuvMatrix Matrix, bool FullRange) Apply(AVFrame* frame)
    {
        var tags = Infer(frame->colorspace, frame->color_range, (AVPixelFormat)frame->format, frame->height);
        frame->colorspace = tags.Matrix switch
        {
            YuvMatrix.Bt709 => AVColorSpace.AVCOL_SPC_BT709,
            YuvMatrix.Bt2020 => AVColorSpace.AVCOL_SPC_BT2020_NCL,
            _ => AVColorSpace.AVCOL_SPC_BT470BG,
        };
        frame->color_range = tags.FullRange ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG;
        return tags;
    }
}
