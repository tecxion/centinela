using Centinela.Core.Platform;

namespace Centinela.Media;

/// <summary>How each system names a shared FFmpeg library (e.g. avcodec version 62).</summary>
public static class FFmpegLibraries
{
    public static string FileName(OsKind os, string library, int version) => os switch
    {
        OsKind.Windows => $"{library}-{version}.dll",
        OsKind.MacOS => $"lib{library}.{version}.dylib",
        _ => $"lib{library}.so.{version}",
    };
}
