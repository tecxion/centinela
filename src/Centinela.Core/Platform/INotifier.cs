namespace Centinela.Core.Platform;

/// <summary>System notifications (outside the tray icon). Call on the UI thread; onClick runs on the UI thread.</summary>
public interface INotifier : IDisposable
{
    void Show(string title, string text, Action? onClick = null);
}
