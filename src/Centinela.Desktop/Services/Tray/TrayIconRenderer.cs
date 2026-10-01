using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Tray;

/// <summary>
/// The tray icons, drawn from the app logo; while recording, a red dot with a white ring is drawn over its bottom
/// right corner (as the Windows version's TrayIcons).
/// </summary>
static class TrayIconRenderer
{
    const int Size = 32;
    static readonly Uri Logo = new("avares://Centinela/Assets/centinela.png");

    /// <summary>Needs the Avalonia platform (UI thread).</summary>
    public static TrayImage Render(bool recording)
    {
        using var logo = new Bitmap(AssetLoader.Open(Logo));
        using var target = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(logo, new Rect(0, 0, Size, Size));
            if (recording)
            {
                var dot = Size * 7 / 16;
                var bounds = new Rect(Size - dot - 1, Size - dot - 1, dot, dot);
                context.DrawEllipse(Brushes.Red, new Pen(Brushes.White, Math.Max(1, Size / 16.0)), bounds);
            }
        }
        var pixels = new byte[Size * Size * 4];
        unsafe
        {
            fixed (byte* buffer = pixels)
                target.CopyPixels(new PixelRect(0, 0, Size, Size), (nint)buffer, pixels.Length, Size * 4);
        }
        // Skia renders in the platform's order: BGRA on Windows, RGBA on some other systems.
        if (target.Format == PixelFormat.Rgba8888) SwapRedAndBlue(pixels);
        if (target.AlphaFormat != AlphaFormat.Unpremul) Unpremultiply(pixels);
        return new TrayImage(Size, Size, pixels);
    }

    /// <summary>Premultiplied BGRA (or RGBA) to straight alpha, in place: each color channel is divided by alpha.</summary>
    public static void Unpremultiply(Span<byte> bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            int alpha = bgra[i + 3];
            if (alpha == 255) continue;
            for (var c = 0; c < 3; c++)
                bgra[i + c] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (bgra[i + c] * 255 + alpha / 2) / alpha);
        }
    }

    static void SwapRedAndBlue(Span<byte> pixels)
    {
        for (var i = 0; i + 3 < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
    }
}
