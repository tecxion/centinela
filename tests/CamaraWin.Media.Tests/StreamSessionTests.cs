using System.Diagnostics;
using CamaraWin.Media.Tests.Rtsp;
using Xunit.Abstractions;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class StreamSessionTests(RtspTestServer server, ITestOutputHelper output)
{
    static readonly TimeSpan Ten = TimeSpan.FromSeconds(10);

    StreamSession Open(string url)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(url);
    }

    [SkippableFact]
    public void Plays_and_publishes_native_size_frames()
    {
        using var session = Open(server.Url("open"));
        var watch = Stopwatch.StartNew();
        session.Start();

        Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 0, Ten), session.LastError);
        output.WriteLine($"First frame after {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.ElapsedMilliseconds < 2500, $"first frame took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(SessionState.Playing, session.State);

        long seq = 0;
        (int w, int h) size = default;
        session.Mailbox.TryRead(ref seq, f => size = (f.Width, f.Height));
        Assert.Equal((640, 360), size);
    }

    [SkippableFact]
    public void Scales_to_target_size()
    {
        using var session = Open(server.Url("open"));
        session.SetTargetSize(320, 320);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 2, Ten), session.LastError);

        long seq = 0;
        (int w, int h) size = default;
        session.Mailbox.TryRead(ref seq, f => size = (f.Width, f.Height));
        Assert.Equal((320, 180), size);
    }

    [SkippableFact]
    public void Authenticates_with_special_character_password()
    {
        using var session = Open(server.Url("secure", RtspTestServer.SecureUser, RtspTestServer.SecurePassword));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);
    }

    [SkippableFact]
    public void Wrong_password_ends_in_AuthFailed()
    {
        using var session = Open(server.Url("secure", RtspTestServer.SecureUser, "wrong"));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.AuthFailed, Ten),
            $"state={session.State} error={session.LastError}");
    }

    [SkippableFact]
    public void Unreachable_camera_goes_to_Reconnecting()
    {
        using var session = Open("rtsp://127.0.0.1:1/none");
        var watch = Stopwatch.StartNew();
        session.Start();
        // On Windows FFmpeg does not notice a refused TCP connect and waits for the full 5 s socket
        // timeout (rtsp "timeout" option), so allow a margin above it.
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Reconnecting, TimeSpan.FromSeconds(8)),
            $"state={session.State} error={session.LastError}");
        output.WriteLine($"Reconnecting after {watch.ElapsedMilliseconds} ms ({session.LastError})");
        Assert.Equal(0, session.Mailbox.Sequence);
    }

    [SkippableFact]
    public void Stop_is_fast_and_final()
    {
        using var session = Open(server.Url("open"));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);

        var watch = Stopwatch.StartNew();
        session.Stop();
        Assert.True(watch.ElapsedMilliseconds < 2000, $"stop took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(SessionState.Stopped, session.State);
    }

    [SkippableFact]
    public void Reconnects_after_server_restart()
    {
        using var session = Open(server.Url("open"));
        var states = new List<SessionState>();
        session.StateChanged += s => { lock (states) states.Add(s); };
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);

        server.RestartServer();

        Assert.True(TestUtil.WaitFor(() =>
        {
            lock (states) return states.Contains(SessionState.Reconnecting) && session.State == SessionState.Playing;
        }, TimeSpan.FromSeconds(25)), $"state={session.State} error={session.LastError}");
    }
}
