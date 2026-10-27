using Centinela.Core;

namespace Centinela.Core.Tests;

public class NotificationGateTests
{
    static readonly DateTime Noon = new(2026, 9, 28, 12, 0, 0);
    static readonly Camera Cam = new() { Name = "A" };

    [Fact]
    public void Connection_lost_shows_and_sounds_only_if_enabled()
    {
        Assert.Equal(new NoticeDecision(true, false), NotificationGate.Decide(NoticeKind.ConnectionLost, Cam, new AppSettings(), true, Noon));
        Assert.Equal(new NoticeDecision(true, true), NotificationGate.Decide(NoticeKind.ConnectionLost, Cam, new AppSettings { SoundOnConnectionLost = true }, true, Noon));
        Assert.Equal(new NoticeDecision(false, false), NotificationGate.Decide(NoticeKind.ConnectionLost, new Camera { ConnectionAlerts = false }, new AppSettings { SoundOnConnectionLost = true }, true, Noon));
    }

    [Fact]
    public void Recovery_never_sounds() =>
        Assert.Equal(new NoticeDecision(true, false), NotificationGate.Decide(NoticeKind.ConnectionRecovered, Cam, new AppSettings { SoundOnConnectionLost = true }, false, Noon));

    [Fact]
    public void Motion_only_when_the_window_is_not_visible()
    {
        var s = new AppSettings { SoundOnMotion = true };
        Assert.Equal(new NoticeDecision(false, false), NotificationGate.Decide(NoticeKind.Motion, Cam, s, windowVisible: true, Noon));
        Assert.Equal(new NoticeDecision(true, true), NotificationGate.Decide(NoticeKind.Motion, Cam, s, windowVisible: false, Noon));
        Assert.Equal(new NoticeDecision(false, false), NotificationGate.Decide(NoticeKind.Motion, new Camera { MotionAlerts = false }, s, false, Noon));
    }

    [Theory]
    [InlineData("23:00", "07:00", "23:30", true)]
    [InlineData("23:00", "07:00", "03:00", true)]
    [InlineData("23:00", "07:00", "07:00", false)]
    [InlineData("23:00", "07:00", "22:59", false)]
    [InlineData("13:00", "15:00", "13:00", true)]
    [InlineData("13:00", "15:00", "15:00", false)]
    [InlineData("08:00", "08:00", "08:00", false)]
    public void Quiet_hours(string from, string to, string at, bool quiet) =>
        Assert.Equal(quiet, NotificationGate.IsQuiet(new AppSettings { QuietHoursEnabled = true, QuietFrom = from, QuietTo = to }, TimeSpan.Parse(at)));

    [Fact]
    public void Quiet_hours_silence_everything_and_can_be_disabled()
    {
        var s = new AppSettings { QuietHoursEnabled = true, QuietFrom = "11:00", QuietTo = "13:00", SoundOnConnectionLost = true, SoundOnMotion = true };
        Assert.Equal(default, NotificationGate.Decide(NoticeKind.ConnectionLost, Cam, s, false, Noon));
        Assert.Equal(default, NotificationGate.Decide(NoticeKind.Motion, Cam, s, false, Noon));
        s.QuietHoursEnabled = false;
        Assert.True(NotificationGate.Decide(NoticeKind.Motion, Cam, s, false, Noon).Show);
    }
}
