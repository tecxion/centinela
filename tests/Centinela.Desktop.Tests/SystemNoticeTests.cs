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

public class RecordingFailureNoticeTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void On_windows_only_while_hidden_in_the_tray_as_always(bool hiddenInTray, bool expected)
    {
        // Minimized without being in the tray: no notice, as before.
        Assert.Equal(expected, SystemNotice.ForRecordingFailure(OsKind.Windows, hiddenInTray, windowVisible: false, canShow: true));
        Assert.False(SystemNotice.ForRecordingFailure(OsKind.Windows, hiddenInTray: false, windowVisible: true, canShow: true));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Elsewhere_whenever_the_window_is_not_visible_and_a_notice_can_show(OsKind os)
    {
        // Minimized with no tray (the system's notifications show it), or hidden in the tray.
        Assert.True(SystemNotice.ForRecordingFailure(os, hiddenInTray: false, windowVisible: false, canShow: true));
        Assert.True(SystemNotice.ForRecordingFailure(os, hiddenInTray: true, windowVisible: false, canShow: true));
        Assert.False(SystemNotice.ForRecordingFailure(os, hiddenInTray: false, windowVisible: true, canShow: true));
        Assert.False(SystemNotice.ForRecordingFailure(os, hiddenInTray: false, windowVisible: false, canShow: false));
    }
}

public class OutsideWindowNoticeTests
{
    [Fact]
    public void On_windows_exactly_hidden_in_the_tray_as_always()
    {
        foreach (var hiddenInTray in new[] { false, true })
            foreach (var hasTray in new[] { false, true })
                foreach (var windowVisible in new[] { false, true })
                    foreach (var canShow in new[] { false, true })
                        Assert.Equal(hiddenInTray,
                            SystemNotice.OutsideWindow(hiddenInTray, OsKind.Windows, hasTray, windowVisible, canShow));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Elsewhere_also_minimized_without_a_tray_when_a_notice_can_show(OsKind os)
    {
        Assert.True(SystemNotice.OutsideWindow(hiddenInTray: true, os, hasTray: true, windowVisible: false, canShowSystemNotice: true));
        Assert.True(SystemNotice.OutsideWindow(hiddenInTray: false, os, hasTray: false, windowVisible: false, canShowSystemNotice: true));
        // Visible, minimized with a tray (a toast, as on Windows), or no way to show a system notice: inside the window.
        Assert.False(SystemNotice.OutsideWindow(hiddenInTray: false, os, hasTray: false, windowVisible: true, canShowSystemNotice: true));
        Assert.False(SystemNotice.OutsideWindow(hiddenInTray: false, os, hasTray: true, windowVisible: false, canShowSystemNotice: true));
        Assert.False(SystemNotice.OutsideWindow(hiddenInTray: false, os, hasTray: false, windowVisible: false, canShowSystemNotice: false));
    }
}

public class TrayHintTests
{
    [Fact]
    public void Windows_keeps_its_double_click() =>
        Assert.Equal("Las grabaciones continúan. Doble clic en el icono para abrirla; «Salir» la cierra.", TrayTexts.HiddenHint(OsKind.Windows));

    [Fact]
    public void Linux_a_single_click() =>
        Assert.Equal("Las grabaciones continúan. Haz clic en el icono para abrirla; «Salir» la cierra.", TrayTexts.HiddenHint(OsKind.Linux));

    [Fact]
    public void MacOS_the_menu_bar_menu_or_the_Dock() =>
        Assert.Equal("Las grabaciones continúan. Para volver a abrirla, usa «Abrir» en el menú del icono de la barra de menús o haz clic en Centinela en el Dock.",
            TrayTexts.HiddenHint(OsKind.MacOS));

    [Theory]
    [InlineData(OsKind.Windows, "Centinela sigue en la bandeja")]
    [InlineData(OsKind.Linux, "Centinela sigue en la bandeja")]
    [InlineData(OsKind.MacOS, "Centinela sigue en la barra de menús")]
    public void The_title_names_where_it_stays(OsKind os, string title) =>
        Assert.Equal(title, TrayTexts.HiddenTitle(os));
}

public class TrayAvailabilityWaitTests
{
    [Fact]
    public void The_answer_when_it_comes_in_time()
    {
        Assert.True(TrayAvailabilityWait.Get(Task.FromResult(true), TimeSpan.FromSeconds(1)));
        Assert.False(TrayAvailabilityWait.Get(Task.FromResult(false), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void No_tray_when_the_check_takes_too_long_or_fails()
    {
        Assert.False(TrayAvailabilityWait.Get(new TaskCompletionSource<bool>().Task, TimeSpan.FromMilliseconds(50)));
        Assert.False(TrayAvailabilityWait.Get(Task.FromException<bool>(new InvalidOperationException()), TimeSpan.FromSeconds(1)));
    }
}
