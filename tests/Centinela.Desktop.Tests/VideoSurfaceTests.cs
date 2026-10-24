using Avalonia;
using Avalonia.Media;
using Centinela.Desktop.Video;
using Centinela.Media;

namespace Centinela.Desktop.Tests;

/// <summary>Zoom and image rectangle of a surface whose OpenGL child never gets Ready (no running app here).</summary>
public class VideoSurfaceTests
{
    static VideoSurface Showing(StreamSession session, Action? onContentRect = null)
    {
        session.Mailbox.Publish(session.Mailbox.Rent(160, 90));
        var surface = new VideoSurface { Session = session };
        if (onContentRect is not null) surface.ContentRectChanged += onContentRect;
        Layout(surface, 400, 400);
        Assert.True(surface.TryReadFrame(_ => { }));
        return surface;
    }

    static void Layout(VideoSurface surface, double width, double height)
    {
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void Zoom_is_a_render_transform_and_moves_the_draw_rect()
    {
        using var session = new StreamSession("rtsp://camera/");
        var surface = Showing(session);
        Assert.Equal(new Rect(0, 87.5, 400, 225), surface.ContentRect);

        var zoom = Matrix.CreateScale(2, 2) * Matrix.CreateTranslation(-100, -50);
        surface.SetZoom(zoom);
        Assert.Equal(zoom, Assert.IsType<MatrixTransform>(surface.RenderTransform).Matrix);
        Assert.Equal(surface.ContentRect.TransformToAABB(zoom), surface.DrawRect);

        surface.SetZoom(null);
        Assert.Null(surface.RenderTransform);
        Assert.Equal(surface.ContentRect, surface.DrawRect);
    }

    [Fact]
    public void A_frame_taken_by_OpenGL_raises_ContentRectChanged_later_and_a_resize_at_once()
    {
        using var session = new StreamSession("rtsp://camera/");
        var raised = 0;
        var surface = Showing(session, () => raised++);
        // Posted out of the GL render (the tests' dispatcher never runs it); the rect itself is already there.
        Assert.Equal(0, raised);
        Assert.Equal(new Rect(0, 87.5, 400, 225), surface.ContentRect);

        Layout(surface, 800, 450);
        Assert.Equal(1, raised);
        Assert.Equal(new Rect(0, 0, 800, 450), surface.ContentRect);
    }
}
