using System.Runtime.InteropServices;

namespace CamaraWin.App;

/// <summary>One CamaraWin per user session: a later launch wakes the first one and exits.</summary>
static class SingleInstance
{
    const string MutexName = @"Local\CamaraWin.SingleInstance";
    const string EventName = @"Local\CamaraWin.Activate";
    // Held for the life of the process; the OS releases the mutex when the process ends.
    static Mutex? _mutex;
    static EventWaitHandle? _activate;

    const int AnyProcess = -1;   // ASFW_ANY

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// True for the first instance; a later instance signals the first one and gets false.
    /// A launch while the first instance is exiting (mutex still held during its exit wait) is absorbed:
    /// the signal is ignored and nothing opens, so the user simply launches again.
    /// </summary>
    public static bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            return true;
        }
        try
        {
            using var existing = EventWaitHandle.OpenExisting(EventName);
            // This launch owns the foreground; let the first instance bring its window forward.
            AllowSetForegroundWindow(AnyProcess);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // First instance is still starting; nothing to activate.
        }
        return false;
    }

    /// <summary>Calls <paramref name="onActivate"/> on a background thread each time a later instance starts.</summary>
    public static void ListenForActivation(Action onActivate)
    {
        var handle = _activate ?? throw new InvalidOperationException("Not the first instance.");
        new Thread(() =>
        {
            while (handle.WaitOne()) onActivate();
        }) { IsBackground = true, Name = "SingleInstance" }.Start();
    }
}
