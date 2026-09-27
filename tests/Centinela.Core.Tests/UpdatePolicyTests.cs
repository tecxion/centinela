using Centinela.Core;

namespace Centinela.Core.Tests;

public class UpdatePolicyTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Auto_check_once_a_day_when_enabled()
    {
        Assert.True(UpdatePolicy.ShouldAutoCheck(new AppSettings(), Now));
        Assert.False(UpdatePolicy.ShouldAutoCheck(new AppSettings { LastUpdateCheck = Now.AddHours(-23) }, Now));
        Assert.True(UpdatePolicy.ShouldAutoCheck(new AppSettings { LastUpdateCheck = Now.AddHours(-24) }, Now));
        Assert.False(UpdatePolicy.ShouldAutoCheck(new AppSettings { CheckUpdatesOnStartup = false }, Now));
    }

    [Fact]
    public void A_last_check_in_the_future_does_not_block_checks()
    {
        Assert.True(UpdatePolicy.ShouldAutoCheck(new AppSettings { LastUpdateCheck = Now.AddDays(3) }, Now));
    }

    [Fact]
    public void Notify_only_for_available_and_not_skipped()
    {
        var available = new UpdateResult(UpdateStatus.UpdateAvailable, new Version(1, 3, 0));
        Assert.True(UpdatePolicy.ShouldNotify(available, new AppSettings()));
        Assert.False(UpdatePolicy.ShouldNotify(available, new AppSettings { SkippedVersion = "1.3.0" }));
        Assert.True(UpdatePolicy.ShouldNotify(available, new AppSettings { SkippedVersion = "1.2.5" }));
        Assert.False(UpdatePolicy.ShouldNotify(new UpdateResult(UpdateStatus.UpToDate), new AppSettings()));
    }

    [Theory]
    [InlineData(UpdateStatus.UpToDate, true)] [InlineData(UpdateStatus.UpdateAvailable, true)]
    [InlineData(UpdateStatus.NoReleases, true)] [InlineData(UpdateStatus.Failed, false)]
    public void Only_answers_from_GitHub_count_as_checked(UpdateStatus status, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.CountsAsChecked(new UpdateResult(status)));
}
