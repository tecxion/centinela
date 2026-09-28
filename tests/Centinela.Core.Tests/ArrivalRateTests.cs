using Centinela.Core.Playback;

namespace Centinela.Core.Tests;

public class ArrivalRateTests
{
    [Fact]
    public void A_link_that_keeps_up_is_near_one()
    {
        var r = new ArrivalRate();
        for (var i = 0; i < 300; i++) r.Add(100 + i * 0.04, i * 0.04);
        Assert.Equal(1.0, r.Ratio, 2);
    }

    [Fact]
    public void A_link_delivering_20_of_25_fps_is_at_80_percent()
    {
        var r = new ArrivalRate();
        for (var i = 0; i < 300; i++) r.Add(100 + i * 0.05, i * 0.04);
        Assert.Equal(0.8, r.Ratio, 2);
    }

    [Fact]
    public void Unknown_until_two_seconds_are_measured()
    {
        var r = new ArrivalRate();
        for (var i = 0; i < 40; i++) r.Add(100 + i * 0.04, i * 0.04);
        Assert.True(double.IsNaN(r.Ratio));
    }

    [Fact]
    public void A_timestamp_jump_restarts_the_window()
    {
        var r = new ArrivalRate();
        for (var i = 0; i < 100; i++) r.Add(100 + i * 0.04, i * 0.04);
        r.Add(104, 5000);
        Assert.True(double.IsNaN(r.Ratio));
    }
}
