using Centinela.Core.Platform;
using Centinela.Desktop.Services.Tray;

namespace Centinela.Desktop.Tests;

public class SystemNoticeTests
{
    [Theory]
    [InlineData(true, true, SystemNoticeTarget.Tray)]
    [InlineData(true, false, SystemNoticeTarget.Tray)]
    [InlineData(false, true, SystemNoticeTarget.Notifier)]
    [InlineData(false, false, SystemNoticeTarget.None)]
    public void The_tray_first_then_the_notifier_else_nothing(bool hasTray, bool hasNotifier, SystemNoticeTarget expected) =>
        Assert.Equal(expected, SystemNotice.Choose(hasTray, hasNotifier));
}

public class WindowCloseTests
{
    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void With_a_tray_the_x_hides(OsKind os) =>
        Assert.Equal(CloseAction.Hide, WindowClose.Decide(exitRequested: false, systemExit: false, hasTray: true, os));

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Without_a_tray_on_linux_and_macos_the_x_minimizes(OsKind os) =>
        Assert.Equal(CloseAction.Minimize, WindowClose.Decide(exitRequested: false, systemExit: false, hasTray: false, os));

    [Fact]
    public void Without_a_tray_on_windows_the_x_closes_as_always() =>
        Assert.Equal(CloseAction.Close, WindowClose.Decide(exitRequested: false, systemExit: false, hasTray: false, OsKind.Windows));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Salir_or_the_end_of_the_session_always_closes(bool exitRequested, bool systemExit)
    {
        foreach (var os in Enum.GetValues<OsKind>())
            foreach (var hasTray in new[] { false, true })
                Assert.Equal(CloseAction.Close, WindowClose.Decide(exitRequested, systemExit, hasTray, os));
    }
}
