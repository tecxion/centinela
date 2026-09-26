namespace CamaraWin.Media;

public static class FrameGeometry
{
    /// <summary>Largest even size with the source aspect ratio that fits the box. Never upscales. Box of 0 = native.</summary>
    public static (int Width, int Height) FitSize(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)
    {
        if (boxWidth <= 0 || boxHeight <= 0 || (boxWidth >= sourceWidth && boxHeight >= sourceHeight))
            return (sourceWidth, sourceHeight);
        var scale = Math.Min(boxWidth / (double)sourceWidth, boxHeight / (double)sourceHeight);
        return (Even(sourceWidth * scale), Even(sourceHeight * scale));
    }

    static int Even(double value) => Math.Max(2, (int)Math.Round(value) & ~1);
}
