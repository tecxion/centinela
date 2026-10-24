namespace Centinela.Media;

/// <summary>How a mailbox frame stores its pixels.</summary>
public enum FrameLayout
{
    /// <summary>BGRA, 4 bytes per pixel: painted by the processor as a bitmap.</summary>
    Bgra,
    /// <summary>NV12: the Y plane, then U/V pairs at half size; the graphics card turns it into RGB.</summary>
    Nv12,
}

/// <summary>A decoded image in a mailbox, rows packed tight. NV12 frames also say which colours they use.</summary>
public sealed class VideoFrame
{
    public VideoFrame(int width, int height, FrameLayout layout = FrameLayout.Bgra)
    {
        Width = width;
        Height = height;
        Layout = layout;
        Stride = layout == FrameLayout.Bgra ? width * 4 : width;
        ChromaWidth = (width + 1) / 2;
        ChromaHeight = (height + 1) / 2;
        Data = new byte[layout == FrameLayout.Bgra ? Stride * height : width * height + ChromaWidth * 2 * ChromaHeight];
    }

    public int Width { get; }
    public int Height { get; }
    public FrameLayout Layout { get; }
    /// <summary>Bytes per row of the BGRA image, or of the NV12 Y plane.</summary>
    public int Stride { get; }
    /// <summary>NV12: U/V pairs per row (each row is ChromaWidth·2 bytes) and rows of the U/V plane.</summary>
    public int ChromaWidth { get; }
    public int ChromaHeight { get; }
    /// <summary>NV12: where the U/V plane starts in <see cref="Data"/>.</summary>
    public int ChromaOffset => Width * Height;
    public byte[] Data { get; }
    /// <summary>NV12: the colours to turn it into RGB with; the producer sets them for every frame it publishes.</summary>
    public YuvMatrix Matrix { get; set; }
    public bool FullRange { get; set; }
}
