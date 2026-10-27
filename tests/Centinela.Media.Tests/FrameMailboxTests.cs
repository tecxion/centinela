using Centinela.Media;

namespace Centinela.Media.Tests;

public class FrameMailboxTests
{
    [Fact]
    public void Frame_has_bgra_stride()
    {
        var f = new VideoFrame(10, 3);
        Assert.Equal(40, f.Stride);
        Assert.Equal(120, f.Data.Length);
    }

    [Fact]
    public void TryRead_returns_false_when_nothing_published()
    {
        long seq = 0;
        Assert.False(new FrameMailbox().TryRead(ref seq, _ => { }));
    }

    [Fact]
    public void Published_frame_is_read_once()
    {
        var mailbox = new FrameMailbox();
        var frame = mailbox.Rent(4, 2);
        frame.Data[0] = 7;
        mailbox.Publish(frame);

        long seq = 0;
        byte seen = 0;
        Assert.True(mailbox.TryRead(ref seq, f => seen = f.Data[0]));
        Assert.Equal(7, seen);
        Assert.False(mailbox.TryRead(ref seq, _ => { }));
    }

    [Fact]
    public void Latest_frame_wins()
    {
        var mailbox = new FrameMailbox();
        var first = mailbox.Rent(4, 2);
        first.Data[0] = 1;
        mailbox.Publish(first);
        var second = mailbox.Rent(4, 2);
        second.Data[0] = 2;
        mailbox.Publish(second);

        long seq = 0;
        byte seen = 0;
        mailbox.TryRead(ref seq, f => seen = f.Data[0]);
        Assert.Equal(2, seen);
        Assert.Equal(2, mailbox.Sequence);
    }

    [Fact]
    public void Rent_reuses_the_displaced_buffer_when_size_matches()
    {
        var mailbox = new FrameMailbox();
        var a = mailbox.Rent(4, 2);
        mailbox.Publish(a);
        var b = mailbox.Rent(4, 2);
        mailbox.Publish(b);
        Assert.Same(a, mailbox.Rent(4, 2));
    }

    [Fact]
    public void Rent_allocates_when_size_changes()
    {
        var mailbox = new FrameMailbox();
        var a = mailbox.Rent(4, 2);
        mailbox.Publish(a);
        mailbox.Publish(mailbox.Rent(4, 2));
        var c = mailbox.Rent(8, 4);
        Assert.NotSame(a, c);
        Assert.Equal(32, c.Stride);
    }

    [Fact]
    public void Nv12_frame_has_a_y_plane_and_a_half_size_chroma_plane()
    {
        var f = new VideoFrame(6, 4, FrameLayout.Nv12);
        Assert.Equal((6, 3, 2, 24), (f.Stride, f.ChromaWidth, f.ChromaHeight, f.ChromaOffset));
        Assert.Equal(6 * 4 + 3 * 2 * 2, f.Data.Length);
    }

    [Fact]
    public void Odd_nv12_sizes_round_the_chroma_plane_up()
    {
        var f = new VideoFrame(5, 3, FrameLayout.Nv12);
        Assert.Equal((3, 2), (f.ChromaWidth, f.ChromaHeight));
        Assert.Equal(15 + 3 * 2 * 2, f.Data.Length);
    }

    [Fact]
    public void Rent_does_not_reuse_a_frame_of_another_layout()
    {
        var mailbox = new FrameMailbox();
        mailbox.Publish(mailbox.Rent(4, 2));
        mailbox.Publish(mailbox.Rent(4, 2)); // the first one is now the spare
        var frame = mailbox.Rent(4, 2, FrameLayout.Nv12);
        Assert.Equal(FrameLayout.Nv12, frame.Layout);
    }
}
