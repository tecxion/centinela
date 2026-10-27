using Centinela.Core;

namespace Centinela.Core.Tests;

public class MotionTrackerTests
{
    static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    static MotionSample Yes(double f = 0.05) => new(true, f);
    static readonly MotionSample No = new(false, 0);

    [Fact]
    public void Starts_alert_eligible_then_ends_after_3_s_without_motion_with_peak()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        var start = t.Update(Yes(0.02), T0);
        Assert.Equal((MotionTransition.Started, true), (start.Transition, start.AlertEligible));
        Assert.True(t.IsActive);
        Assert.Equal(MotionTransition.None, t.Update(Yes(0.09), T0.AddSeconds(1)).Transition);
        Assert.Equal(MotionTransition.None, t.Update(No, T0.AddSeconds(3.5)).Transition);
        var end = t.Update(No, T0.AddSeconds(4));
        Assert.Equal(MotionTransition.Ended, end.Transition);
        Assert.Equal(new MotionEvent(T0, T0.AddSeconds(1), 0.09), end.Event);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void Second_event_within_cooldown_of_a_shown_alert_is_not_eligible()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        Assert.True(t.Update(Yes(), T0).AlertEligible);
        t.MarkAlerted(T0);
        t.Update(No, T0.AddSeconds(5));
        var again = t.Update(Yes(), T0.AddSeconds(30));
        Assert.Equal((MotionTransition.Started, false), (again.Transition, again.AlertEligible));
        t.Update(No, T0.AddSeconds(40));
        Assert.True(t.Update(Yes(), T0.AddSeconds(61)).AlertEligible);
    }

    [Fact]
    public void Eligible_alert_that_was_not_shown_does_not_consume_the_cooldown()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        Assert.True(t.Update(Yes(), T0).AlertEligible);   // hidden (quiet hours, alerts off…): no MarkAlerted
        t.Update(No, T0.AddSeconds(5));
        Assert.True(t.Update(Yes(), T0.AddSeconds(10)).AlertEligible);
    }

    [Fact]
    public void Cooldown_can_change_at_runtime()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(900));
        t.Update(Yes(), T0);
        t.MarkAlerted(T0);
        t.Update(No, T0.AddSeconds(5));
        t.Cooldown = TimeSpan.FromSeconds(30);
        Assert.True(t.Update(Yes(), T0.AddSeconds(31)).AlertEligible);
    }

    [Fact]
    public void Flush_ends_an_active_event_and_is_a_no_op_when_idle()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        Assert.Equal(MotionTransition.None, t.Flush(T0).Transition);
        t.Update(Yes(0.04), T0);
        var flushed = t.Flush(T0.AddSeconds(2));
        Assert.Equal(MotionTransition.Ended, flushed.Transition);
        Assert.Equal(0.04, flushed.Event!.Peak);
    }
}
