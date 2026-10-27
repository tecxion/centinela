using Centinela.Core;
using Centinela.Desktop.Services.Notifications;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

public class NotificationOptionsTests
{
    static Camera Cam(string name, int order, int cooldown = 60) =>
        new() { Name = name, Order = order, MotionCooldownSeconds = cooldown };

    [Fact]
    public void RowsFor_orders_by_Order_and_normalizes_the_cooldown()
    {
        var rows = NotificationOptions.RowsFor([Cam("B", 2, 45), Cam("A", 1)]);

        Assert.Equal(["A", "B"], rows.Select(r => r.Name));
        Assert.Equal(60, rows[1].Cooldown.Value);
        Assert.Equal("1 min", rows[1].Cooldown.Label);
    }

    [Fact]
    public void Rows_hold_the_same_option_instances_as_the_lists()
    {
        var row = NotificationOptions.RowsFor([Cam("A", 0, 300)])[0];

        Assert.Contains(NotificationOptions.Cooldowns, c => ReferenceEquals(c, row.Cooldown));
        Assert.Contains(NotificationOptions.Sensitivities, s => ReferenceEquals(s, row.Sensitivity));
    }

    [Fact]
    public void Apply_writes_rows_to_their_cameras_ignores_missing_ones_and_sets_the_sounds()
    {
        var first = Cam("A", 0);
        var second = Cam("B", 1);
        var rows = NotificationOptions.RowsFor([first, second, Cam("Gone", 2)]);
        var row = rows[1];
        row.MotionEnabled = true;
        row.Sensitivity = NotificationOptions.Sensitivities[2];
        row.Cooldown = NotificationOptions.Cooldowns[3];
        row.ConnectionAlerts = false;
        row.MotionAlerts = false;
        var settings = new AppSettings();

        NotificationOptions.Apply(rows, [first, second], settings, new QuietHours(true, true, true, " 23:30 ", "06:15"));

        Assert.True(second.MotionEnabled);
        Assert.Equal(MotionSensitivity.High, second.MotionSensitivity);
        Assert.Equal(900, second.MotionCooldownSeconds);
        Assert.False(second.ConnectionAlerts);
        Assert.False(second.MotionAlerts);
        Assert.False(first.MotionEnabled);
        Assert.True(first.ConnectionAlerts);
        Assert.True(settings.SoundOnConnectionLost);
        Assert.True(settings.SoundOnMotion);
        Assert.True(settings.QuietHoursEnabled);
        Assert.Equal("23:30", settings.QuietFrom);
        Assert.Equal("06:15", settings.QuietTo);
    }

    [Theory]
    [InlineData("07:00", true)]
    [InlineData(" 23:59 ", true)]
    [InlineData("24:00", false)]
    [InlineData("7:00", false)]
    [InlineData("ab:cd", false)]
    [InlineData("", false)]
    public void IsValidTime_accepts_only_HH_mm(string text, bool valid)
    {
        Assert.Equal(valid, NotificationOptions.IsValidTime(text));
    }

    [Fact]
    public void Cooldown_labels()
    {
        Assert.Equal(["30 s", "1 min", "5 min", "15 min"], NotificationOptions.Cooldowns.Select(c => c.Label));
    }

    [Fact]
    public void Sensitivity_labels()
    {
        Assert.Equal(["Baja", "Media", "Alta"], NotificationOptions.Sensitivities.Select(s => s.Label));
    }
}
