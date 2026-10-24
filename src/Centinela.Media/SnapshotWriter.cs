using FFmpeg.AutoGen;

namespace Centinela.Media;

public static unsafe class SnapshotWriter
{
    public static void SavePng(AVFrame* source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_PNG);
        if (codec is null) throw new InvalidOperationException("FFmpeg sin codificador PNG");

        var encoder = ffmpeg.avcodec_alloc_context3(codec);
        var rgb = ffmpeg.av_frame_alloc();
        var packet = ffmpeg.av_packet_alloc();
        SwsContext* sws = null;
        try
        {
            encoder->width = source->width;
            encoder->height = source->height;
            encoder->pix_fmt = AVPixelFormat.AV_PIX_FMT_RGB24;
            encoder->time_base = new AVRational { num = 1, den = 1 };
            FFmpegException.ThrowIfError(ffmpeg.avcodec_open2(encoder, codec, null), "open png encoder");

            ColorTags.Apply(source);
            rgb->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
            rgb->width = source->width;
            rgb->height = source->height;
            rgb->colorspace = AVColorSpace.AVCOL_SPC_RGB;
            rgb->color_range = AVColorRange.AVCOL_RANGE_JPEG;
            rgb->color_primaries = source->color_primaries;
            rgb->color_trc = source->color_trc;
            FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(rgb, 0), "alloc rgb frame");

            sws = ffmpeg.sws_alloc_context();
            if (sws is null) throw new InvalidOperationException("sws_alloc_context failed");
            sws->flags = (uint)SwsFlags.SWS_BICUBIC;
            FFmpegException.ThrowIfError(ffmpeg.sws_scale_frame(sws, rgb, source), "scale png");

            FFmpegException.ThrowIfError(ffmpeg.avcodec_send_frame(encoder, rgb), "encode png");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_receive_packet(encoder, packet), "encode png");
            using var file = File.Create(path);
            file.Write(new ReadOnlySpan<byte>(packet->data, packet->size));
        }
        finally
        {
            ffmpeg.sws_free_context(&sws);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&rgb);
            ffmpeg.avcodec_free_context(&encoder);
        }
    }
}
