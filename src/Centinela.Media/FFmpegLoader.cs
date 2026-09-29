using Centinela.Core.Platform;
using FFmpeg.AutoGen;

namespace Centinela.Media;

public static class FFmpegLoader
{
    static readonly object Gate = new();
    static bool _loaded;

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static void Initialize(string? directory = null)
    {
        lock (Gate)
        {
            if (_loaded) return;
            var dir = directory ?? DefaultDirectory;
            var avcodec = Path.Combine(dir, FFmpegLibraries.FileName(OsInfo.Current, "avcodec", ffmpeg.LibraryVersionMap["avcodec"]));
            if (!File.Exists(avcodec))
                throw new FileNotFoundException(
                    $"No se encuentran las librerías de FFmpeg en '{dir}'. Ejecuta el script de tools/ que las descarga y recompila.", avcodec);

            ffmpeg.RootPath = dir;
            DynamicallyLoadedBindings.Initialize();
            FFmpegLog.Install();
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            _loaded = true;
        }
    }

    public static string Version => ffmpeg.av_version_info();
}
