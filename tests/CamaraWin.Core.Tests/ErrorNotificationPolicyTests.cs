using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class ErrorNotificationPolicyTests
{
    readonly Guid _cam = Guid.NewGuid();

    [Theory]
    [InlineData(StreamErrorKind.AuthFailed)]
    [InlineData(StreamErrorKind.NotFound)]
    [InlineData(StreamErrorKind.CameraBusy)]
    public void Immediate_kinds_notify_once(StreamErrorKind kind)
    {
        var p = new ErrorNotificationPolicy();
        Assert.True(p.ShouldNotify(_cam, kind));
        Assert.False(p.ShouldNotify(_cam, kind));
    }

    [Theory]
    [InlineData(StreamErrorKind.Unreachable)]
    [InlineData(StreamErrorKind.Stalled)]
    [InlineData(StreamErrorKind.ServerError)]
    [InlineData(StreamErrorKind.Unknown)]
    public void Transient_kinds_notify_on_third_consecutive_failure(StreamErrorKind kind)
    {
        var p = new ErrorNotificationPolicy();
        Assert.False(p.ShouldNotify(_cam, kind));
        Assert.False(p.ShouldNotify(_cam, kind));
        Assert.True(p.ShouldNotify(_cam, kind));
        Assert.False(p.ShouldNotify(_cam, kind));
    }

    [Fact]
    public void Playing_resets_and_reports_recovery_only_after_a_notice()
    {
        var p = new ErrorNotificationPolicy();
        p.ShouldNotify(_cam, StreamErrorKind.Unreachable);
        Assert.False(p.OnPlaying(_cam)); // no notice was shown
        p.ShouldNotify(_cam, StreamErrorKind.AuthFailed);
        Assert.True(p.OnPlaying(_cam));
        Assert.True(p.ShouldNotify(_cam, StreamErrorKind.AuthFailed)); // notifies again after recovery
    }

    [Fact]
    public void Cameras_are_independent()
    {
        var p = new ErrorNotificationPolicy();
        Assert.True(p.ShouldNotify(_cam, StreamErrorKind.AuthFailed));
        Assert.True(p.ShouldNotify(Guid.NewGuid(), StreamErrorKind.AuthFailed));
    }
}
