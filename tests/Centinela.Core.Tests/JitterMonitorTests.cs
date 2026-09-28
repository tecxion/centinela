using Centinela.Core.Playback;

namespace Centinela.Core.Tests;

public class JitterMonitorTests
{
    const double Frame = 0.04;

    /// <summary>Feeds <paramref name="count"/> frames at 25 fps; returns the next pts and arrival.</summary>
    static (double Pts, double Arrival) Steady(JitterMonitor m, double pts, double arrival, int count, List<bool>? results = null)
    {
        for (var i = 0; i < count; i++)
        {
            var r = m.Add(arrival, pts);
            results?.Add(r);
            pts += Frame;
            arrival += Frame;
        }
        return (pts, arrival);
    }

    /// <summary>The link freezes for <paramref name="stall"/> seconds, then the waiting frames arrive at once.</summary>
    static (double Pts, double Arrival) Stall(JitterMonitor m, double pts, double arrival, double stall, List<bool> results)
    {
        arrival += stall;
        var waiting = (int)(stall / Frame);
        for (var i = 0; i < waiting; i++)
        {
            results.Add(m.Add(arrival, pts));
            pts += Frame;
        }
        return (pts, arrival + Frame);
    }

    [Fact]
    public void Steady_arrivals_with_small_irregularities_are_stable()
    {
        var m = new JitterMonitor();
        var arrival = 100.0;
        for (var i = 0; i < 1000; i++) Assert.False(m.Add(arrival + i * Frame + i % 5 * 0.03, i * Frame));
        Assert.Equal(0, m.WorstStall);
    }

    [Fact]
    public void One_stall_is_not_enough_but_two_are()
    {
        var m = new JitterMonitor();
        var results = new List<bool>();
        var (pts, arrival) = Steady(m, 0, 100, 50);
        (pts, arrival) = Stall(m, pts, arrival, 0.8, results);
        (pts, arrival) = Steady(m, pts, arrival, 100, results);
        Assert.DoesNotContain(true, results);
        (pts, arrival) = Stall(m, pts, arrival, 1.0, results);
        Assert.True(results[^1]);
        Assert.Equal(1.0, m.WorstStall, 2);
    }

    [Fact]
    public void Stalls_further_apart_than_the_window_do_not_add_up()
    {
        var m = new JitterMonitor(windowSeconds: 30);
        var results = new List<bool>();
        var (pts, arrival) = Steady(m, 0, 100, 10);
        (pts, arrival) = Stall(m, pts, arrival, 0.8, results);
        (pts, arrival) = Steady(m, pts, arrival, 1000, results); // 40 s
        Stall(m, pts, arrival, 0.8, results);
        Assert.DoesNotContain(true, results);
    }

    [Fact]
    public void Timestamp_jumps_are_not_stalls()
    {
        var m = new JitterMonitor();
        var (_, arrival) = Steady(m, 0, 100, 10);
        Assert.False(m.Add(arrival, 3600));
        Assert.False(m.Add(arrival + Frame, 3600 + Frame));
        Assert.False(m.Add(arrival + 2 * Frame, 0));
        Assert.Equal(0, m.WorstStall);
    }

    [Fact]
    public void Reset_forgets_stalls_and_the_previous_packet()
    {
        var m = new JitterMonitor(stallCount: 1);
        m.Add(100, 0);
        Assert.True(m.Add(101, Frame));
        m.Reset();
        Assert.False(m.Add(200, 2 * Frame));
        Assert.Equal(0, m.WorstStall);
    }
}
