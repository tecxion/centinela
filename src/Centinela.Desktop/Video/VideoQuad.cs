using Avalonia;

namespace Centinela.Desktop.Video;

/// <summary>Where the video is drawn: the 4 vertices (x, y, s, t) of a triangle strip in OpenGL clip space.</summary>
static class VideoQuad
{
    /// <param name="content">The image's rectangle in the view, in DIPs (letterboxed, then zoomed).</param>
    /// <param name="view">The view's size in DIPs.</param>
    public static float[] Vertices(Rect content, Size view)
    {
        float X(double x) => (float)(2 * x / view.Width - 1);
        float Y(double y) => (float)(1 - 2 * y / view.Height); // DIPs grow downwards, clip space upwards
        // Top-left, bottom-left, top-right, bottom-right; texture row 0 is the image's top row.
        return
        [
            X(content.Left), Y(content.Top), 0, 0,
            X(content.Left), Y(content.Bottom), 0, 1,
            X(content.Right), Y(content.Top), 1, 0,
            X(content.Right), Y(content.Bottom), 1, 1,
        ];
    }
}
