using Centinela.Core.Platform;
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>The GPU decoder of each system and the pixel format its frames come in.</summary>
public static class HardwareAcceleration
{
    public static (AVHWDeviceType Device, AVPixelFormat Format) Current { get; } = For(OsInfo.Current);

    public static (AVHWDeviceType Device, AVPixelFormat Format) For(OsKind os) => os switch
    {
        OsKind.Windows => (AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, AVPixelFormat.AV_PIX_FMT_D3D11),
        OsKind.MacOS => (AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX),
        _ => (AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVPixelFormat.AV_PIX_FMT_VAAPI),
    };
}
