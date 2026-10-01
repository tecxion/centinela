using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Tray;

/// <summary>Where a system notice (outside the window) goes.</summary>
public enum SystemNoticeTarget { Tray, Notifier, None }

/// <summary>
/// The single path of system notices: the tray icon's when there is one; otherwise the system's notifications
/// (a Linux desktop without a tray); otherwise none.
/// </summary>
static class SystemNotice
{
    public static SystemNoticeTarget Choose(bool hasTray, bool hasNotifier) =>
        hasTray ? SystemNoticeTarget.Tray : hasNotifier ? SystemNoticeTarget.Notifier : SystemNoticeTarget.None;

    /// <summary>
    /// A recording failed (the status line already shows it): also a system notice when the window is out of sight.
    /// Windows, as always, only while hidden in the tray; elsewhere also minimized, when a notice can show (the
    /// system's notifications stand in for a missing tray).
    /// </summary>
    public static bool ForRecordingFailure(OsKind os, bool hiddenInTray, bool windowVisible, bool canShow) =>
        os == OsKind.Windows ? hiddenInTray : !windowVisible && canShow;
}
