using Centinela.Core.Playback;

namespace Centinela.Core.Tests;

public class PlayoutClockTests
{
    const double Frame = 0.04;

    [Fact]
    public void Starts_by_buffering_the_target_delay()
    {
        var c = new PlayoutClock(initialDelay: 0.5);
        Assert.Equal(PlayoutAction.Wait, c.Next(headPts: 0, newestPts: 0.2, now: 0).Action);
        Assert.True(c.Buffering);
        // Half a second of video is in: playback starts with the oldest packet.
        Assert.Equal(PlayoutAction.Present, c.Next(headPts: 0, newestPts: 0.5, now: 1).Action);
        Assert.False(c.Buffering);
    }

    [Fact]
    public void Packets_play_at_their_own_pace_after_start()
    {
        var c = new PlayoutClock(initialDelay: 0.5);
        c.Next(0, 0.5, now: 10);
        // Next frame is due 40 ms after the first one, even though it is already here.
        var d = c.Next(headPts: Frame, newestPts: 0.6, now: 10);
        Assert.Equal(PlayoutAction.Wait, d.Action);
        Assert.Equal(Frame, d.WaitSeconds, 3);
        Assert.Equal(PlayoutAction.Present, c.Next(Frame, 0.6, now: 10 + Frame).Action);
    }

    [Fact]
    public void Running_dry_rebuffers_and_grows_the_delay_up_to_the_maximum()
    {
        var c = new PlayoutClock(initialDelay: 0.5, maxDelay: 2.0);
        c.Next(0, 0.5, now: 0);
        // Nothing new arrives: the clock passes the newest packet and the queue is empty.
        c.Idle(newestPts: 0.5, now: 0.8);
        Assert.True(c.Buffering);
        Assert.Equal(0.75, c.TargetDelay, 3);
        for (var i = 0; i < 10; i++) c.Starve();
        Assert.Equal(2.0, c.TargetDelay, 3);
    }

    [Fact]
    public void Idle_within_the_frame_gap_does_not_rebuffer()
    {
        var c = new PlayoutClock(initialDelay: 0.5);
        c.Next(0, 0.5, now: 0);
        c.Idle(newestPts: 0.5, now: 0.55);
        Assert.False(c.Buffering);
    }

    [Fact]
    public void A_backlog_far_beyond_the_delay_jumps_ahead_and_skips_the_old_packets()
    {
        var c = new PlayoutClock(initialDelay: 0.5);
        c.Next(0, 0.5, now: 0);
        // A burst delivers 3 s more video: playing it all would put us 3 s behind.
        var d = c.Next(headPts: Frame, newestPts: 3.5, now: Frame);
        Assert.Equal(PlayoutAction.Skip, d.Action);
        // Packets near the new position play again.
        Assert.Equal(PlayoutAction.Present, c.Next(headPts: 3.0, newestPts: 3.5, now: Frame).Action);
    }

    [Fact]
    public void A_moderate_backlog_catches_up_by_playing_slightly_faster()
    {
        var c = new PlayoutClock(initialDelay: 0.5);
        c.Next(0, 0.5, now: 0);
        // 0.5 s over the target: frames come sooner than their 40 ms spacing.
        var d = c.Next(headPts: Frame, newestPts: 1.0 + Frame, now: 0);
        Assert.Equal(PlayoutAction.Wait, d.Action);
        Assert.True(d.WaitSeconds < Frame * 0.95);
    }

    [Fact]
    public void A_timestamp_jump_restarts_buffering_from_the_new_packet()
    {
        var c = new PlayoutClock(initialDelay: 0.5);
        c.Next(0, 0.5, now: 0);
        Assert.Equal(PlayoutAction.Wait, c.Next(headPts: 5000, newestPts: 5000.1, now: 0.1).Action);
        Assert.True(c.Buffering);
        Assert.Equal(PlayoutAction.Present, c.Next(headPts: 5000, newestPts: 5000.5, now: 0.2).Action);
    }
}
