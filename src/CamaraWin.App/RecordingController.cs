using System.Windows.Threading;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public enum RecordingStatus { Off, Connecting, Recording, Paused }

/// <summary>
/// Owns one headless mainstream recording session per camera, independent of which tiles are on
/// screen (layout changes and tray mode never stop a recording). Call from the UI thread only.
/// </summary>
public sealed class RecordingController(Dispatcher dispatcher)
{
    sealed class Entry(Camera camera, StreamSession session)
    {
        public Camera Camera { get; } = camera;
        public StreamSession Session { get; } = session;
        public RecordingStatus Status { get; set; } = RecordingStatus.Connecting;
    }

    readonly Dictionary<Guid, Entry> _active = [];
    readonly List<Task> _finalizing = [];

    public event Action<Guid, RecordingStatus>? StatusChanged;
    public event Action<Camera, StreamError>? ErrorOccurred;
    public event Action<Camera, SessionState>? SessionStateChanged;
    /// <summary>Progress notices (status bar only).</summary>
    public event Action<string, string?>? Notify;
    /// <summary>A recording could not start, stopped on its own or could not be saved; shown even while in the tray.</summary>
    public event Action<string>? Failure;

    public bool IsRecording(Guid cameraId) => _active.ContainsKey(cameraId);
    public bool AnyRecording => _active.Count > 0;
    public RecordingStatus StatusOf(Guid cameraId) => _active.TryGetValue(cameraId, out var e) ? e.Status : RecordingStatus.Off;

    public void Start(Camera camera)
    {
        if (_active.ContainsKey(camera.Id)) return;
        string url;
        try { url = StreamUrlBuilder.Build(camera, StreamKind.Main); }
        catch (InvalidOperationException)
        {
            Failure?.Invoke($"No se puede grabar {camera.Name}: falta la URL RTSP.");
            return;
        }
        var name = camera.Name;
        var session = new StreamSession(url, camera.UseUdp, decode: false);
        var entry = new Entry(camera, session);
        session.StartRecording(() => AppPaths.RecordingFile(name, DateTime.Now));
        session.RecordingFailed += message => dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(entry)) return;
            StopObserved(camera.Id, name);
            Failure?.Invoke($"Grabación de {name} detenida: {message}");
        });
        session.ErrorOccurred += error => dispatcher.BeginInvoke(() =>
        {
            if (IsCurrent(entry)) ErrorOccurred?.Invoke(camera, error);
        });
        session.StateChanged += state => dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(entry)) return;
            SessionStateChanged?.Invoke(camera, state);
            if (state == SessionState.AuthFailed)
            {
                StopObserved(camera.Id, name);
                Failure?.Invoke($"No se pudo grabar {name}: contraseña incorrecta.");
                return;
            }
            var status = state switch
            {
                SessionState.Playing => RecordingStatus.Recording,
                SessionState.Reconnecting => RecordingStatus.Paused,
                _ => entry.Status,
            };
            if (status == entry.Status) return;
            var pausedNow = status == RecordingStatus.Paused;
            entry.Status = status;
            StatusChanged?.Invoke(camera.Id, status);
            if (pausedNow) Notify?.Invoke($"Grabación de {name} en pausa: reconectando…", null);
        });
        _active[camera.Id] = entry;
        session.Start();
        StatusChanged?.Invoke(camera.Id, RecordingStatus.Connecting);
        Notify?.Invoke($"Grabando {name}…", null);
    }

    /// <summary>Stops and finalizes in the background; the task faults with RecordingException if the file could not be closed.</summary>
    public Task Stop(Guid cameraId)
    {
        if (!_active.Remove(cameraId, out var entry)) return Task.CompletedTask;
        StatusChanged?.Invoke(cameraId, RecordingStatus.Off);
        entry.Session.RequestStop();
        var finalize = Task.Run(() => FinalizeAsync(entry.Session));
        _finalizing.Add(finalize);
        finalize.ContinueWith(_ => dispatcher.BeginInvoke(() => _finalizing.Remove(finalize)), TaskScheduler.Default);
        return finalize;
    }

    /// <summary>Stop with user-facing "guardando…" / "guardada" / error notices.</summary>
    public void StopWithNotice(Guid cameraId)
    {
        if (!_active.TryGetValue(cameraId, out var entry)) return;
        var name = entry.Camera.Name;
        Notify?.Invoke($"Guardando grabación de {name}…", null);
        Stop(cameraId).ContinueWith(done => dispatcher.BeginInvoke(() =>
        {
            if (done.Exception is { } ex)
                Failure?.Invoke($"No se pudo cerrar la grabación de {name}: {ex.GetBaseException().Message}");
            else
                Notify?.Invoke($"Grabación de {name} guardada en {AppPaths.RecordingsDirectory}", AppPaths.RecordingsDirectory);
        }), TaskScheduler.Default);
    }

    /// <summary>Stop after an automatic failure: the file is still finalized, and a finalize fault is reported too.</summary>
    void StopObserved(Guid cameraId, string name) =>
        Stop(cameraId).ContinueWith(done => dispatcher.BeginInvoke(() =>
            Failure?.Invoke($"No se pudo cerrar la grabación de {name}: {done.Exception!.GetBaseException().Message}")),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    public void StopAllWithNotice()
    {
        foreach (var id in _active.Keys.ToList()) StopWithNotice(id);
    }

    /// <summary>For app exit: stops every recording; the task completes when all files are finalized.</summary>
    public Task ShutdownAsync()
    {
        foreach (var id in _active.Keys.ToList()) _ = Stop(id);
        return Task.WhenAll(_finalizing.ToArray());
    }

    bool IsCurrent(Entry entry) => _active.TryGetValue(entry.Camera.Id, out var current) && ReferenceEquals(current, entry);

    static async Task FinalizeAsync(StreamSession session)
    {
        try { await session.StopRecordingAsync().ConfigureAwait(false); }
        finally { session.Dispose(); }
    }
}
