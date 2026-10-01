using Centinela.Desktop.Services.Audio;
using Centinela.Desktop.Services.Notices;
using Centinela.Desktop.Services.Tray;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>
/// Notices: toasts inside the window (bottom-right, over the grid), or system notices while the window is out of sight
/// (hidden in the tray; outside Windows also minimized with no tray); and the notice sounds (connection lost, motion).
/// Every system notice goes through <see cref="ShowSystemNotice"/>.
/// </summary>
public partial class MainWindow
{
    NoticeRouter _notices = null!;
    readonly NoticeSoundPlayer _noticeSounds = new();

    void InitNotices()
    {
        // Hidden in the tray; or, outside Windows with no tray, minimized (the system's notifications stand in).
        _notices = new NoticeRouter(
            () => SystemNotice.OutsideWindow(IsHiddenInTray, Os, HasTray, WindowVisible, CanShowSystemNotice),
            Toasts.Show, ShowSystemNotice);
    }

    /// <summary>Whether a system notice would show anywhere (the tray, else the system's notifications).</summary>
    bool CanShowSystemNotice =>
        SystemNotice.Choose(_tray is not null, App.Platform.Notifier is not null) != SystemNoticeTarget.None;

    /// <summary>
    /// A notice outside the window: the tray's, else the system's notifications (Linux without a tray), else none.
    /// A click on it runs <paramref name="onClick"/>, or opens the window without one.
    /// </summary>
    void ShowSystemNotice(string title, string text, Action? onClick = null)
    {
        if (_closing) return;
        switch (SystemNotice.Choose(_tray is not null, App.Platform.Notifier is not null))
        {
            case SystemNoticeTarget.Tray:
                _tray!.ShowNotice(title, text, onClick);
                break;
            case SystemNoticeTarget.Notifier:
                App.Platform.Notifier!.Show(title, text, onClick ?? (() => UiThread.Post(ShowFromTray)));
                break;
        }
    }
}
