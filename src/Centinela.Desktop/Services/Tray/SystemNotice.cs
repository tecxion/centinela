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
}
