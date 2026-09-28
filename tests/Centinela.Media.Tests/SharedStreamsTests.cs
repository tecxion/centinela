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

    [SkippableFact]
    public void Session_decodes_at_the_largest_requested_size()
    {
        using var streams = Open(out var url);
        using var small = streams.Acquire(Key, url, false, 160, 90);
        using var big = streams.Acquire(Key, url, false, 640, 360);
        VideoFrame? last = null;
        long seq = 0;
        Assert.True(TestUtil.WaitFor(() => big.Session.Mailbox.TryRead(ref seq, f => last = f) && last!.Width == 640, TimeSpan.FromSeconds(10)));
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
