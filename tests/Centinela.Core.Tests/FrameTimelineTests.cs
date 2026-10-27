using Centinela.Core.Playback;

namespace Centinela.Core.Tests;

public class FrameTimelineTests
{
    [Fact]
    public void Learns_the_delivered_frame_rate_and_spaces_frames_by_it()
    {
        var t = new FrameTimeline();
        double key = 0;
        for (var i = 0; i < 150; i++) key = t.Add(100 + i / 15.0, newFrame: true); // 15 fps for 10 s
        Assert.Equal(1 / 15.0, t.Interval, 4);
        Assert.Equal(1 / 15.0, t.Add(100 + 150 / 15.0, newFrame: true) - key, 4);
    }

    [Fact]
    public void A_stall_and_burst_keeps_frames_evenly_spaced_on_the_timeline()
    {
        var t = new FrameTimeline();
        for (var i = 0; i < 150; i++) t.Add(100 + i / 15.0, newFrame: true);
        // One second of frames arrives together after a stall.
        var keys = Enumerable.Range(0, 15).Select(_ => t.Add(111, newFrame: true)).ToList();
        var gaps = keys.Zip(keys.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, g => Assert.InRange(g, 0.05, 0.08));
    }

    [Fact]
    public void Packets_of_the_same_frame_share_its_position()
    {
        var t = new FrameTimeline();
        var first = t.Add(100, newFrame: true);
        Assert.Equal(first, t.Add(100.001, newFrame: false));
        Assert.True(t.Add(100.04, newFrame: true) > first);
    }

    [Fact]
    public void Uses_the_default_interval_until_two_seconds_are_measured()
    {
        var t = new FrameTimeline(defaultInterval: 0.04);
        for (var i = 0; i < 10; i++) t.Add(100 + i * 0.1, newFrame: true);
        Assert.Equal(0.04, t.Interval);
    }

    [Fact]
    public void Reset_starts_a_new_timeline()
    {
        var t = new FrameTimeline();
        t.Add(100, true);
        t.Add(100.04, true);
        t.Reset();
        Assert.Equal(0, t.Add(200, true));
    }
}
