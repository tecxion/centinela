using System.Globalization;
using Centinela.Core;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.Services.Ui;
using Centinela.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>
/// One camera tile: its live session (own for the main stream, a shared lease for the substream), the status text
/// and the stats. Session events arrive on session threads and are posted to the UI thread.
/// </summary>
sealed partial class TileViewModel : ObservableObject
{
    static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    readonly SharedStreamLease? _lease;
    readonly Action<SessionState>? _onState;
    readonly Action<StreamError>? _onError;
    readonly Action<double>? _onSmoothing;
    readonly Action<StreamInfo>? _onInfo;
    StreamErrorKind? _errorKind;
    Task? _shutdown;
    bool _disposed;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _big;
    [ObservableProperty] private bool _showStats;
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private bool _statsStale;
    [ObservableProperty] private bool _smoothingVisible;
    [ObservableProperty] private string _smoothingToolTip = "";

    /// <param name="manage">False for fullscreen tiles: no drag, no editing.</param>
    public TileViewModel(Camera camera, StreamKind kind, bool manage, StreamLeases? leases)
    {
        Camera = camera;
        Kind = kind;
        Manage = manage;
        _name = camera.Name;
        if (kind == StreamKind.Sub && leases is not null)
        {
            _lease = leases.AcquireSub(camera, 0, 0);
            Session = _lease?.Session;
        }
        else
        {
            try
            {
                Session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
                Session.Smoothing = camera.Smoothing;
            }
            catch (InvalidOperationException)
            {
                Session = null;
            }
        }
        if (Session is null)
        {
            StatusText = "Falta la URL RTSP";
            return;
        }
        _onState = state => UiThread.Post(() => ShowState(state));
        _onError = error => UiThread.Post(() => ShowError(error));
        _onSmoothing = seconds => UiThread.Post(() => ShowSmoothing(seconds));
        _onInfo = _ => UiThread.Post(UpdateAudio);
        Session.StateChanged += _onState;
        Session.ErrorOccurred += _onError;
        Session.SmoothingChanged += _onSmoothing;
        Session.InfoAvailable += _onInfo;
        ShowSmoothing(Session.SmoothingSeconds);
        StatusText = "Conectando…";
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }
    public bool Manage { get; }
    /// <summary>Null when the camera has no usable URL: the tile only shows the error.</summary>
    public StreamSession? Session { get; }
    /// <summary>True when the session is shared (substream lease); the window reports its errors once.</summary>
    public bool IsShared => _lease is not null;
    /// <summary>True once <see cref="ShutdownAsync"/> was called: the tile is gone.</summary>
    public bool IsShutDown => _disposed;

    /// <summary>Raised on the UI thread each time the stream reports reconnecting or failed authentication.</summary>
    public event Action<TileViewModel>? StreamFailed;
    /// <summary>Raised on the UI thread each time an own (not shared) stream reaches Playing.</summary>
    public event Action<TileViewModel>? Playing;
    /// <summary>Raised on the UI thread for each error of an own stream (shared ones are reported by the window).</summary>
    public event Action<TileViewModel, StreamError>? ErrorReported;

    /// <summary>Starts an own session; a shared one shows where it already is.</summary>
    public void Start()
    {
        if (Session is not { } session) return;
        if (_lease is null)
        {
            session.Start();
            return;
        }
        var current = session.State;
        if (current != SessionState.Playing && session.LastErrorKind is { } lastError) _errorKind = lastError;
        ShowState(current == SessionState.Idle ? SessionState.Connecting : current);
        UpdateAudio();
    }

    /// <summary>The decode box in device pixels; ignored until the tile has a real size.</summary>
    public void SetTargetSize(int width, int height)
    {
        if (width < 1 || height < 1) return;
        if (_lease is not null) _lease.SetTargetSize(width, height);
        else Session?.SetTargetSize(width, height);
    }

    partial void OnShowStatsChanged(bool value)
    {
        if (value) RefreshStats();
    }

    /// <summary>fps · ms · GPU/CPU (and smoothing); called twice a second by the view while stats are shown.</summary>
    public void RefreshStats()
    {
        if (Session is null || _disposed) return;
        if (Session.State != SessionState.Playing)
        {
            // Latency and decoder describe the last stream that played; not meaningful until it plays again.
            StatsText = "— fps";
            StatsStale = true;
            return;
        }
        var s = Session.Stats;
        StatsText = $"{s.Fps:0} fps · {s.LatencyMs:0} ms · {(s.HardwareDecoding ? "GPU" : "CPU")}"
            + (s.SmoothingSeconds > 0
                ? string.Create(CultureInfo.InvariantCulture, $" · suav. {s.SmoothingSeconds:0.##} s · cortes {s.Rebuffers}")
                : "");
        StatsStale = s.SinceLastFrame > TimeSpan.FromSeconds(1);
    }

    /// <summary>Internal for the tests; session states arrive through <see cref="UiThread"/>.</summary>
    internal void ShowState(SessionState state)
    {
        if (_disposed) return;
        if (state == SessionState.AuthFailed)
        {
            _errorKind = StreamErrorKind.AuthFailed;
            ShowFixButton = Manage;
        }
        StatusText = TileStatus.Text(state, _errorKind, Camera);
        IsPlaying = state == SessionState.Playing;
        if (state is SessionState.Reconnecting or SessionState.AuthFailed) StreamFailed?.Invoke(this);
        if (state == SessionState.Playing)
        {
            _errorKind = null;
            ShowFixButton = false;
            if (_lease is null) Playing?.Invoke(this);
        }
    }

    void ShowError(StreamError error)
    {
        if (_disposed) return;
        _errorKind = error.Kind;
        ShowFixButton = Manage && error.Kind == StreamErrorKind.AuthFailed;
        StatusText = TileStatus.Text(Session?.State ?? SessionState.Reconnecting, _errorKind, Camera);
        if (_lease is null) ErrorReported?.Invoke(this, error);
    }

    void ShowSmoothing(double seconds)
    {
        if (_disposed) return;
        SmoothingVisible = seconds > 0;
        SmoothingToolTip = string.Create(Spanish, $"Imagen suavizada: {seconds:0.##} s de retraso para que se vea sin tirones.");
    }

    /// <summary>
    /// Stops the live view (own session disposed off the UI thread) or releases the shared lease. UI thread;
    /// repeated calls return the same task.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        _disposed = true;
        if (Session is { } session)
        {
            session.StateChanged -= _onState;
            session.ErrorOccurred -= _onError;
            session.SmoothingChanged -= _onSmoothing;
            session.InfoAvailable -= _onInfo;
        }
        AudioOn = false;
        if (_lease is not null) return _shutdown = _lease.ReleaseAsync();
        Session?.RequestStop();
        return _shutdown = Session is null ? Task.CompletedTask : Task.Run(Session.Dispose);
    }
}
