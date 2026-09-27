using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Centinela.Core;
using Centinela.Media;

namespace Centinela.App;

public sealed partial class CameraTile : UserControl, IDisposable
{
    // Null when the camera has no usable URL (custom camera without one): the tile only shows the error.
    readonly StreamSession? _session;
    readonly bool _manage;
    Task? _shutdown;
    WriteableBitmap? _bitmap;
    long _frameSequence;
    bool _disposed;
    bool _firstFrameRaised;

    public CameraTile(Camera camera, StreamKind kind, bool manage = true)
    {
        InitializeComponent();
        Camera = camera;
        Kind = kind;
        _manage = manage;
        NameLabel.Text = camera.Name;
        EditButton.Visibility = DeleteButton.Visibility = manage ? Visibility.Visible : Visibility.Collapsed;
        MouseEnter += (_, _) => Actions.Visibility = Visibility.Visible;
        MouseLeave += (_, _) =>
        {
            Actions.Visibility = Visibility.Collapsed;
            // A press that is released outside the tile is not a click.
            _dragStart = null;
        };
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            if (!_disposed) Clicked?.Invoke(this);
        };

        string url;
        try
        {
            url = StreamUrlBuilder.Build(camera, kind);
        }
        catch (InvalidOperationException)
        {
            StatusLabel.Text = "Falta la URL RTSP";
            Video.Opacity = 0.4;
            SnapshotButton.IsEnabled = RecordButton.IsEnabled = false;
            return;
        }
        _session = new StreamSession(url, camera.UseUdp);
        _session.StateChanged += state => Dispatcher.BeginInvoke(() => ShowState(state));
        _session.ErrorOccurred += error => Dispatcher.BeginInvoke(() => ShowError(error));
        SizeChanged += (_, _) => UpdateTargetSize();
        CompositionTarget.Rendering += OnRendering;
        ShowState(SessionState.Connecting);
        _session.Start();
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }
    /// <summary>False when the camera has no usable URL, so the tile will never show video.</summary>
    public bool HasStream => _session is not null;

    public event Action<CameraTile>? EditRequested;
    public event Action<CameraTile>? DeleteRequested;
    public event Action<CameraTile>? RecordRequested;
    public event Action<string, string?>? Notify;

    const string DragFormat = "Centinela.CameraId";
    static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    static readonly Brush DropBorder = Brushes.DodgerBlue;
    Point? _dragStart;

    public event Action<CameraTile>? FullscreenRequested;
    public event Action<Guid, Guid>? SwapRequested;
    /// <summary>Raised once, on the UI thread, after the first video frame has been painted.</summary>
    public event Action<CameraTile>? FirstFrameShown;
    /// <summary>
    /// A single left click that did not start a drag, raised once the double-click time has passed
    /// without a second press (so a double-click never also counts as a click).
    /// </summary>
    public event Action<CameraTile>? Clicked;
    /// <summary>Raised on the UI thread each time the stream reports reconnecting or failed authentication.</summary>
    public event Action<CameraTile>? StreamFailed;
    /// <summary>Raised on the UI thread for every connection error of the live stream (Detail already sanitized).</summary>
    public event Action<CameraTile, StreamError>? ErrorReported;
    /// <summary>Raised on the UI thread each time the live stream reaches Playing.</summary>
    public event Action<CameraTile>? PlayingReached;

    // The last error's kind until the stream plays again; its translation stays on screen while retrying.
    StreamErrorKind? _errorKind;

    readonly DispatcherTimer _clickTimer;

    [DllImport("user32.dll")]
    static extern uint GetDoubleClickTime();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            _dragStart = null;
            FullscreenRequested?.Invoke(this);
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // _dragStart is cleared when a drag starts or on double-click, so reaching here with it set means a plain click.
        if (_dragStart is null) return;
        _dragStart = null;
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_manage || _dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        _clickTimer.Stop();
        DragDrop.DoDragDrop(this, new DataObject(DragFormat, Camera.Id.ToString()), DragDropEffects.Move);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (_manage && e.Data.GetDataPresent(DragFormat)) Frame.BorderBrush = DropBorder;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        Frame.BorderBrush = NormalBorder;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = _manage && e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        Frame.BorderBrush = NormalBorder;
        if (_manage && e.Data.GetData(DragFormat) is string raw && Guid.TryParse(raw, out var source) && source != Camera.Id)
            SwapRequested?.Invoke(source, Camera.Id);
    }

    void OnRendering(object? sender, EventArgs e)
    {
        if (_session is null) return;
        var painted = false;
        _session.Mailbox.TryRead(ref _frameSequence, frame =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                Video.Source = _bitmap;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Data, frame.Stride, 0);
            painted = true;
        });
        if (painted && !_firstFrameRaised)
        {
            _firstFrameRaised = true;
            FirstFrameShown?.Invoke(this);
        }
    }

    void UpdateTargetSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _session?.SetTargetSize((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
    }

    void ShowError(StreamError error)
    {
        if (_disposed) return;
        _errorKind = error.Kind;
        var text = ErrorCenter.Translate(Camera, error.Kind);
        StatusLabel.Text = error.Kind == StreamErrorKind.AuthFailed ? text.Short : $"{text.Short} · reintentando";
        FixButton.Visibility = error.Kind == StreamErrorKind.AuthFailed && _manage ? Visibility.Visible : Visibility.Collapsed;
        ErrorReported?.Invoke(this, error);
    }

    void ShowState(SessionState state)
    {
        if (_disposed) return;
        switch (state)
        {
            case SessionState.Connecting or SessionState.Reconnecting when _errorKind is not null:
                break; // keep the translated error while retrying
            case SessionState.Connecting:
                StatusLabel.Text = "Conectando…";
                break;
            case SessionState.Reconnecting:
                StatusLabel.Text = "Reconectando…";
                break;
            case SessionState.AuthFailed:
                // The session reports the error just before this state, so the label is normally set already.
                _errorKind = StreamErrorKind.AuthFailed;
                StatusLabel.Text = ErrorCenter.Translate(Camera, StreamErrorKind.AuthFailed).Short;
                FixButton.Visibility = _manage ? Visibility.Visible : Visibility.Collapsed;
                break;
            case SessionState.Stopped:
                StatusLabel.Text = "Detenida";
                break;
            default:
                StatusLabel.Text = "";
                break;
        }
        Video.Opacity = state == SessionState.Playing ? 1 : 0.4;
        if (state is SessionState.Reconnecting or SessionState.AuthFailed) StreamFailed?.Invoke(this);
        if (state == SessionState.Playing)
        {
            _errorKind = null;
            FixButton.Visibility = Visibility.Collapsed;
            PlayingReached?.Invoke(this);
        }
    }

    void Edit_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this);

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this);

    async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        var path = AppPaths.SnapshotFile(Camera.Name, DateTime.Now);
        SnapshotButton.IsEnabled = false;
        try
        {
            var saved = await SaveMainStreamSnapshotAsync(path);
            if (_disposed) return;
            Notify?.Invoke(saved ? $"Captura guardada: {path}" : "Aún no hay imagen para capturar.",
                File.Exists(path) ? path : null);
        }
        catch (Exception ex)
        {
            if (!_disposed) Notify?.Invoke($"No se pudo guardar la captura: {ex.Message}", null);
        }
        finally
        {
            SnapshotButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Grid tiles show the substream; try to grab one full-resolution frame from the mainstream instead
    /// (up to 3 s, or until it fails authentication), falling back to the substream frame.
    /// </summary>
    async Task<bool> SaveMainStreamSnapshotAsync(string path)
    {
        if (_session is not { } session) return false;
        if (Kind == StreamKind.Main) return await session.SaveSnapshotAsync(path);
        var main = new StreamSession(StreamUrlBuilder.Build(Camera, StreamKind.Main), Camera.UseUdp);
        try
        {
            main.SetTargetSize(2, 2);
            main.Start();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && !_disposed && main.State != SessionState.AuthFailed)
            {
                if (main.Mailbox.Sequence > 0) return await main.SaveSnapshotAsync(path);
                await Task.Delay(100);
            }
            return !_disposed && await session.SaveSnapshotAsync(path);
        }
        finally
        {
            // Stopping joins the session thread: keep that off the UI thread.
            _ = Task.Run(main.Dispose);
        }
    }

    void Record_Click(object sender, RoutedEventArgs e) => RecordRequested?.Invoke(this);

    /// <summary>Shows the recording owned by the window's RecordingController (dot and button).</summary>
    public void SetRecordingStatus(RecordingStatus status)
    {
        if (status == RecordingStatus.Off)
        {
            RecDot.Visibility = Visibility.Collapsed;
            RecordButton.Content = "⏺";
            RecordButton.ToolTip = "Grabar";
            return;
        }
        ShowRecordingDot(paused: status != RecordingStatus.Recording, status switch
        {
            RecordingStatus.Recording => "Grabando",
            RecordingStatus.Paused => "Grabación en pausa: reconectando…",
            _ => "Grabación: conectando…",
        });
        RecordButton.Content = "⏹";
        RecordButton.ToolTip = "Detener grabación";
    }

    /// <summary>Solid red while recording; hollow while the recording session (re)connects.</summary>
    void ShowRecordingDot(bool paused, string tooltip)
    {
        RecDot.Visibility = Visibility.Visible;
        RecDot.Fill = paused ? Brushes.Transparent : Brushes.Red;
        RecDot.Stroke = Brushes.Red;
        RecDot.StrokeThickness = paused ? 2 : 0;
        RecDot.Opacity = paused ? 0.7 : 1;
        RecDot.ToolTip = tooltip;
    }

    DispatcherTimer? _statsTimer;

    /// <summary>Shows the live stream's fps, latency and decoder in the bottom-left corner (refreshed twice a second).</summary>
    public bool ShowStats
    {
        get => _statsTimer is not null;
        set
        {
            if (value == ShowStats || _session is null || _disposed) return;
            if (value)
            {
                _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _statsTimer.Tick += (_, _) => RefreshStats();
                _statsTimer.Start();
                StatsBorder.Visibility = Visibility.Visible;
                RefreshStats();
            }
            else
            {
                _statsTimer!.Stop();
                _statsTimer = null;
                StatsBorder.Visibility = Visibility.Collapsed;
            }
        }
    }

    void RefreshStats()
    {
        if (_session is null || _disposed) return;
        if (_session.State != SessionState.Playing)
        {
            // Latency and decoder describe the last stream that played; not meaningful until it plays again.
            StatsLabel.Text = "— fps";
            StatsLabel.Foreground = Brushes.Orange;
            return;
        }
        var s = _session.Stats;
        StatsLabel.Text = $"{s.Fps:0} fps · {s.LatencyMs:0} ms · {(s.HardwareDecoding ? "GPU" : "CPU")}";
        StatsLabel.Foreground = s.SinceLastFrame > TimeSpan.FromSeconds(1) ? Brushes.Orange : Brushes.White;
    }

    public void RequestStop() => _session?.RequestStop();

    /// <summary>
    /// Stops the live view and disposes its session off the UI thread (recordings are owned by
    /// RecordingController). Call on the UI thread; repeated calls return the same task.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        _disposed = true;
        _clickTimer.Stop();
        _statsTimer?.Stop();
        CompositionTarget.Rendering -= OnRendering;
        var session = _session;
        session?.RequestStop();
        _shutdown = session is null ? Task.CompletedTask : Task.Run(session.Dispose);
        return _shutdown;
    }

    /// <summary>Starts <see cref="ShutdownAsync"/> without waiting for it.</summary>
    public void Dispose() => _ = ShutdownAsync();
}
