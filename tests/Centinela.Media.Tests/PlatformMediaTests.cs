using Centinela.Core.Platform;
using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public class PlatformMediaTests
{
    [Theory]
    [InlineData(OsKind.Windows, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, AVPixelFormat.AV_PIX_FMT_D3D11)]
    [InlineData(OsKind.MacOS, AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX)]
    [InlineData(OsKind.Linux, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVPixelFormat.AV_PIX_FMT_VAAPI)]
    public void Each_system_has_its_gpu_decoder(OsKind os, AVHWDeviceType device, AVPixelFormat format) =>
        Assert.Equal((device, format), HardwareAcceleration.For(os));

    [Theory]
    [InlineData(OsKind.Windows, "avcodec-62.dll")]
    [InlineData(OsKind.MacOS, "libavcodec.62.dylib")]
    [InlineData(OsKind.Linux, "libavcodec.so.62")]
    public void Library_file_names_follow_each_system(OsKind os, string expected) =>
        Assert.Equal(expected, FFmpegLibraries.FileName(os, "avcodec", 62));
}
