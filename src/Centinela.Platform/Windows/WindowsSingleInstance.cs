using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

/// <summary>
/// A named mutex marks the first instance; a named event wakes it. A CENTINELA_DATA_DIR run passes its own
/// suffix, so a test build never wakes (or yields to) the user's instance.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSingleInstance(string instanceSuffix) : ISingleInstance, IDisposable
{
    const int AnyProcess = -1;   // ASFW_ANY
    readonly string _mutexName = @"Local\Centinela.SingleInstance" + instanceSuffix;
    readonly string _eventName = @"Local\Centinela.Activate" + instanceSuffix;
    // Held for the life of the process; the OS releases the mutex when the process ends.
    Mutex? _mutex;
    EventWaitHandle? _activate;
    bool? _claimed;
    bool _listening;

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <inheritdoc/>
    /// <remarks>
    /// A launch while the first instance is exiting (mutex still held during its exit wait) is absorbed: the signal
    /// is ignored and nothing opens, so the user simply launches again. A repeated call returns the first result.
    /// </remarks>
    public bool TryClaim() => _claimed ??= Claim();

    bool Claim()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (createdNew)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
            return true;
        }
        try
        {
            using var existing = EventWaitHandle.OpenExisting(_eventName);
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

    /// <inheritdoc/>
    /// <remarks>Only the first call listens; later calls keep that listener.</remarks>
    public void ListenForActivation(Action onActivate)
    {
        var handle = _activate ?? throw new InvalidOperationException("Not the first instance.");
        if (_listening) return;
        _listening = true;
        new Thread(() =>
        {
            try
            {
                while (handle.WaitOne()) onActivate();
            }
            catch (ObjectDisposedException)
            {
                // The instance was disposed: stop listening.
            }
        }) { IsBackground = true, Name = "SingleInstance" }.Start();
    }

    public void Dispose()
    {
        _activate?.Dispose();
        _mutex?.Dispose();
    }
}
