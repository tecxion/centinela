using Centinela.Desktop.Services.Audio;
using Centinela.Desktop.Services.Notices;
using Centinela.Desktop.Services.Tray;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>
/// Notices: toasts inside the window (bottom-right, over the grid), or system notices while the window is hidden in
/// the tray; and the notice sounds (connection lost, motion). Every system notice goes through
/// <see cref="ShowSystemNotice"/>.
/// </summary>
public partial class MainWindow
{
    NoticeRouter _notices = null!;
    readonly NoticeSoundPlayer _noticeSounds = new();

    void InitNotices()
    {
        // Without a tray the window is never hidden in it, so the system notice is never needed.
        _notices = new NoticeRouter(() => IsHiddenInTray, Toasts.Show, ShowSystemNotice);
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
