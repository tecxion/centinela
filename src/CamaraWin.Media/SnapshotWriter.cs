using FFmpeg.AutoGen;

namespace CamaraWin.Media;

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

            rgb->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
            rgb->width = source->width;
            rgb->height = source->height;
            FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(rgb, 0), "alloc rgb frame");

            sws = ffmpeg.sws_getContext(source->width, source->height, (AVPixelFormat)source->format,
                source->width, source->height, AVPixelFormat.AV_PIX_FMT_RGB24, (int)SwsFlags.SWS_BICUBIC, null, null, null);
            if (sws is null) throw new InvalidOperationException("sws_getContext failed");
            ffmpeg.sws_scale(sws, source->data.ToArray(), source->linesize.ToArray(), 0, source->height,
                rgb->data.ToArray(), rgb->linesize.ToArray());

            FFmpegException.ThrowIfError(ffmpeg.avcodec_send_frame(encoder, rgb), "encode png");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_receive_packet(encoder, packet), "encode png");
            using var file = File.Create(path);
            file.Write(new ReadOnlySpan<byte>(packet->data, packet->size));
        }
        finally
        {
            ffmpeg.sws_freeContext(sws);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&rgb);
            ffmpeg.avcodec_free_context(&encoder);
        }
    }
}
