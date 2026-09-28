using Centinela.Core;
using Centinela.Media.Tests.Rtsp;

namespace Centinela.Media.Tests;

[Collection("rtsp")]
public sealed class SharedStreamsTests(RtspTestServer server)
{
    static readonly Guid Key = Guid.NewGuid();

    SharedStreams Open(out string url)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        url = server.Url("open");
        return new SharedStreams();
    }

    [SkippableFact]
    public void Two_leases_share_one_session_and_both_read_frames()
    {
        using var streams = Open(out var url);
        using var a = streams.Acquire(Key, url, false, 320, 180);
        using var b = streams.Acquire(Key, url, false, 640, 360);
        Assert.Same(a.Session, b.Session);
        Assert.Equal(1, streams.Count);
        long seqA = 0, seqB = 0;
        Assert.True(TestUtil.WaitFor(() => a.Session.Mailbox.TryRead(ref seqA, _ => { }), TimeSpan.FromSeconds(10)));
        Assert.True(b.Session.Mailbox.TryRead(ref seqB, _ => { }) || TestUtil.WaitFor(() => b.Session.Mailbox.TryRead(ref seqB, _ => { }), TimeSpan.FromSeconds(2)));
    }

    static bool WaitForWidth(SharedStreamLease lease, int width)
    {
        long seq = 0;
        var seen = 0;
        return TestUtil.WaitFor(() => lease.Session.Mailbox.TryRead(ref seq, f => seen = f.Width) && seen == width,
            TimeSpan.FromSeconds(10));
    }

    [SkippableFact]
    public void Session_decodes_at_the_largest_requested_size()
    {
        using var streams = Open(out var url);   // native 640×360
        var big = streams.Acquire(Key, url, false, 640, 360);
        using var small = streams.Acquire(Key, url, false, 160, 90);
        Assert.True(WaitForWidth(small, 640));
        big.Dispose();
        Assert.True(WaitForWidth(small, 160));
        small.SetTargetSize(480, 270);
        Assert.True(WaitForWidth(small, 480));
    }

    [SkippableFact]
    public void A_lease_asking_for_native_size_makes_the_session_decode_natively()
    {
        using var streams = Open(out var url);
        using var small = streams.Acquire(Key, url, false, 160, 90);
        Assert.True(WaitForWidth(small, 160));
        using var native = streams.Acquire(Key, url, false, 0, 0);
        Assert.True(WaitForWidth(small, 640));
    }

    [SkippableFact]
    public void Throwing_onCreated_releases_its_lease_but_a_lease_that_joined_still_gets_a_started_session()
    {
        using var streams = Open(out var url);
        SharedStreamLease? joined = null;
        Assert.Throws<InvalidOperationException>(() => streams.Acquire(Key, url, false, 320, 180, _ =>
        {
            joined = streams.Acquire(Key, url, false, 320, 180);
            throw new InvalidOperationException("wiring failed");
        }));
        Assert.NotNull(joined);
        using (joined)
        {
            Assert.Equal(1, streams.Count);
            Assert.True(TestUtil.WaitFor(() => joined.Session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        }
    }

    [SkippableFact]
    public async Task Throwing_onCreated_with_no_other_lease_leaves_no_session()
    {
        using var streams = Open(out var url);
        StreamSession? session = null;
        Assert.Throws<InvalidOperationException>(() => streams.Acquire(Key, url, false, 320, 180, s =>
        {
            session = s;
            throw new InvalidOperationException("wiring failed");
        }));
        Assert.Equal(0, streams.Count);
        await Task.Delay(300);
        Assert.NotEqual(SessionState.Playing, session!.State);
        Assert.NotEqual(SessionState.Connecting, session.State);
    }

    [SkippableFact]
    public async Task ReleaseAsync_is_idempotent()
    {
        using var streams = Open(out var url);
        var a = streams.Acquire(Key, url, false, 320, 180);
        var b = streams.Acquire(Key, url, false, 320, 180);
        var first = a.ReleaseAsync();
        Assert.Same(first, a.ReleaseAsync());
        a.Dispose();
        Assert.Equal(1, streams.Count);                       // a's extra releases did not release b
        Assert.True(TestUtil.WaitFor(() => b.Session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        var last = b.ReleaseAsync();
        Assert.Same(last, b.ReleaseAsync());
        await last;
        Assert.Equal(SessionState.Stopped, b.Session.State);
        Assert.Equal(0, streams.Count);
    }

    [SkippableFact]
    public async Task Dispose_stops_every_session_and_refuses_new_leases()
    {
        var streams = Open(out var url);
        var a = streams.Acquire(Key, url, false, 320, 180);
        var b = streams.Acquire(Key, server.Url("av"), false, 320, 180);
        Assert.True(TestUtil.WaitFor(() => a.Session.State == SessionState.Playing && b.Session.State == SessionState.Playing,
            TimeSpan.FromSeconds(10)));
        streams.Dispose();
        streams.Dispose();
        Assert.Equal(0, streams.Count);
        Assert.Throws<ObjectDisposedException>(() => streams.Acquire(Key, url, false, 320, 180));
        await Task.WhenAll(a.ReleaseAsync(), b.ReleaseAsync());   // the pending disposals
        Assert.Equal(SessionState.Stopped, a.Session.State);
        Assert.Equal(SessionState.Stopped, b.Session.State);
    }

    [SkippableFact]
    public async Task Last_release_stops_the_session_and_an_earlier_release_does_not()
    {
        using var streams = Open(out var url);
        var a = streams.Acquire(Key, url, false, 320, 180);
        var b = streams.Acquire(Key, url, false, 320, 180);
        Assert.True(TestUtil.WaitFor(() => a.Session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        await a.ReleaseAsync();
        Assert.Equal(SessionState.Playing, b.Session.State);
        await b.ReleaseAsync();
        Assert.Equal(SessionState.Stopped, b.Session.State);
        Assert.Equal(0, streams.Count);
    }

    [SkippableFact]
    public void Different_url_for_the_same_key_gets_its_own_session()
    {
        using var streams = Open(out var url);
        using var a = streams.Acquire(Key, url, false, 320, 180);
        using var b = streams.Acquire(Key, server.Url("av"), false, 320, 180);
        Assert.NotSame(a.Session, b.Session);
    }

    [SkippableFact]
    public void OnCreated_runs_once_per_session()
    {
        using var streams = Open(out var url);
        var created = 0;
        using var a = streams.Acquire(Key, url, false, 320, 180, _ => created++);
        using var b = streams.Acquire(Key, url, false, 320, 180, _ => created++);
        Assert.Equal(1, created);
    }

    [SkippableFact]
    public void Detector_sees_motion_in_the_moving_test_pattern()
    {
        using var streams = Open(out var url);
        using var lease = streams.Acquire(Key, url, false, 320, 180);
        var detector = new FrameDiffDetector(MotionSensitivity.High);
        var gray = GrayScaler.Create();
        long seq = 0;
        var detected = TestUtil.WaitFor(() =>
        {
            Thread.Sleep(200);
            return lease.Session.Mailbox.TryRead(ref seq, f => GrayScaler.FromBgra(f.Data, f.Width, f.Height, f.Stride, gray))
                && detector.Analyze(gray).Motion;
        }, TimeSpan.FromSeconds(10));
        Assert.True(detected);
    }
}
