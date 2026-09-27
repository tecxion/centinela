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
}
