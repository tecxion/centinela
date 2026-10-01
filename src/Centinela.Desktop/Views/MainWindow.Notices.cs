using Centinela.Desktop.Services.Audio;
using Centinela.Desktop.Services.Notices;

namespace Centinela.Desktop.Views;

/// <summary>
/// Notices: toasts inside the window (bottom-right, over the grid), or system notices from the tray while the
/// window is hidden there; and the notice sounds (connection lost, motion).
/// </summary>
public partial class MainWindow
{
    NoticeRouter _notices = null!;
    readonly NoticeSoundPlayer _noticeSounds = new();

    void InitNotices()
    {
        // Without a tray the window is never hidden in it, so the system notice is never needed.
        _notices = new NoticeRouter(() => IsHiddenInTray, Toasts.Show, (title, text, onClick) =>
        {
            if (!_closing) _tray?.ShowNotice(title, text, onClick);
        });
    }
}
