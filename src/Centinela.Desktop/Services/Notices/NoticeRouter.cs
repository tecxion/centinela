namespace Centinela.Desktop.Services.Notices;

/// <summary>
/// Where a notice goes: with the window out of sight (hidden in the tray, or as SystemNotice.OutsideWindow says), a
/// system notice (a click runs the toast's action, or opens the window without one); otherwise a toast inside the window.
/// </summary>
public sealed class NoticeRouter(
    Func<bool> outsideWindow, Action<Toast> showInWindow, Action<string, string, Action?> showSystemNotice)
{
    public void Show(Toast toast)
    {
        if (outsideWindow()) showSystemNotice(toast.Title, toast.Message, toast.OnAction);
        else showInWindow(toast);
    }
}
