using Avalonia.Threading;

namespace Centinela.Desktop.Services.Ui;

/// <summary>Runs work on the UI thread; session events arrive on their own threads.</summary>
static class UiThread
{
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>
    /// Posts to the UI thread. It can be handed out before Avalonia starts (Program.Main): the dispatcher is only
    /// reached when something is posted.
    /// </summary>
    public static SynchronizationContext Context { get; } = new UiContext();

    sealed class UiContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => Dispatcher.UIThread.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state) => Dispatcher.UIThread.Invoke(() => d(state));

        public override SynchronizationContext CreateCopy() => this;
    }
}
