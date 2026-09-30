using Avalonia.Threading;

namespace Centinela.Desktop.Services.Ui;

/// <summary>Runs work on the UI thread; session events arrive on their own threads.</summary>
static class UiThread
{
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
