using Centinela.Core;

namespace Centinela.Desktop.Services.Backup;

/// <summary>
/// The password-less copy written after every camera-list change, off the UI thread. One write at a time; requests
/// made while a copy is being written collapse into one more write of the latest list.
/// </summary>
sealed class AutomaticBackup(Action<string, IReadOnlyList<Camera>> write)
{
    readonly Lock _lock = new();
    (string Folder, List<Camera> Cameras)? _pending;
    bool _running;
    Task _worker = Task.CompletedTask;

    /// <summary>Raised on a background thread with the Spanish message when a copy fails; later copies still run.</summary>
    public event Action<string>? Failed;

    /// <summary>Copies the list now (the caller may change it afterwards) and writes it in the background. UI thread.</summary>
    public void Schedule(string folder, IEnumerable<Camera> cameras)
    {
        var request = (folder, cameras.Select(c => c.Clone()).ToList());
        lock (_lock)
        {
            if (_running)
            {
                _pending = request;
                return;
            }
            // The first request is the worker's own, so a request made right after it cannot replace it.
            _running = true;
            _worker = Task.Run(() => Loop(request));
        }
    }

    /// <summary>Completes when no copy is being written or waiting (for the exit wait).</summary>
    public Task IdleAsync()
    {
        lock (_lock) return _worker;
    }

    void Loop((string Folder, List<Camera> Cameras) request)
    {
        while (true)
        {
            try
            {
                write(request.Folder, request.Cameras);
            }
            catch (Exception ex)
            {
                Failed?.Invoke($"No se pudo guardar la copia automática: {ex.Message}");
            }
            lock (_lock)
            {
                if (_pending is not { } next)
                {
                    _running = false;
                    return;
                }
                request = next;
                _pending = null;
            }
        }
    }
}
