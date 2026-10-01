namespace Centinela.Desktop.Services.Notices;

/// <summary>
/// Where a notice goes: with the window hidden in the tray, a system notice (a click runs the toast's action, or
/// opens the window without one); otherwise a toast inside the window.
/// </summary>
public sealed class NoticeRouter(
    Func<bool> hiddenInTray, Action<Toast> showInWindow, Action<string, string, Action?> showSystemNotice)
{
    public void Show(Toast toast)
    {
        if (hiddenInTray()) showSystemNotice(toast.Title, toast.Message, toast.OnAction);
        else showInWindow(toast);
    }
}
