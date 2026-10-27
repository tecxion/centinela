using Avalonia;
using Centinela.Desktop.Video;

namespace Centinela.Desktop.Tests;

public class VideoQuadTests
{
    [Fact]
    public void Image_filling_the_view_covers_clip_space_top_row_first()
    {
        Assert.Equal(
            new float[] { -1, 1, 0, 0, -1, -1, 0, 1, 1, 1, 1, 0, 1, -1, 1, 1 },
            VideoQuad.Vertices(new Rect(0, 0, 800, 450), new Size(800, 450)));
    }

    [Fact]
    public void Letterboxed_image_sits_in_the_middle()
    {
        var v = VideoQuad.Vertices(new Rect(100, 0, 600, 450), new Size(800, 450));
        Assert.Equal((-0.75f, 1f), (v[0], v[1]));    // top-left
        Assert.Equal((0.75f, -1f), (v[12], v[13]));  // bottom-right
    }

    [Fact]
    public void Zoomed_image_reaches_past_the_view()
    {
        var v = VideoQuad.Vertices(new Rect(-800, -450, 1600, 900), new Size(800, 450));
        Assert.Equal((-3f, 3f), (v[0], v[1]));
        Assert.Equal((1f, -1f), (v[12], v[13]));
    }
}
