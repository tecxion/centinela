namespace Centinela.Core;

/// <summary>An 8-bit luminance image, row-major, no padding.</summary>
public sealed record GrayFrame(int Width, int Height, byte[] Pixels);

public static class GrayScaler
{
    public const int Width = 160, Height = 90;

    public static GrayFrame Create() => new(Width, Height, new byte[Width * Height]);

    /// <summary>
    /// Downscales a BGRA image into <paramref name="target"/> (160×90) by sampling the centre of each block
    /// (integer luma ≈ 0.299 R + 0.587 G + 0.114 B). Cheap enough to run inside the frame mailbox lock.
    /// </summary>
    public static void FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride, GrayFrame target)
    {
        var pixels = target.Pixels;
        for (var ty = 0; ty < target.Height; ty++)
        {
            var sy = Math.Min(height - 1, (int)((ty + 0.5) * height / target.Height));
            var row = sy * stride;
            for (var tx = 0; tx < target.Width; tx++)
            {
                var sx = Math.Min(width - 1, (int)((tx + 0.5) * width / target.Width));
                var i = row + sx * 4;
                pixels[ty * target.Width + tx] = (byte)((29 * bgra[i] + 150 * bgra[i + 1] + 77 * bgra[i + 2]) >> 8);
            }
        }
    }
}
