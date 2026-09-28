using System.Globalization;
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
    // The session in use, own or leased. Null when the camera has no usable URL (custom camera without one):
    // the tile only shows the error.
    readonly StreamSession? _session;
    // Set when the session is shared (substream): the tile releases the lease instead of disposing the session,
    // and the window, not the tile, reports its errors (once per session).
    readonly SharedStreamLease? _lease;
    // Stored so a leased session, which outlives the tile, can be unsubscribed on shutdown.
    readonly Action<SessionState>? _onState;
    readonly Action<StreamError>? _onError;
    readonly Action<StreamInfo>? _onInfo;
    readonly Action<string>? _onAudioFailed;
    readonly bool _manage;
    Task? _shutdown;
    WriteableBitmap? _bitmap;
    long _frameSequence;
    bool _disposed;
    bool _firstFrameRaised;

    /// <param name="acquireShared">
    /// For Sub tiles: leases the camera's shared substream session (null result = no usable URL) instead of
    /// opening a connection of its own.
    /// </param>
    public CameraTile(Camera camera, StreamKind kind, bool manage = true, Func<Camera, SharedStreamLease?>? acquireShared = null)
    {
        InitializeComponent();
        Camera = camera;
        Kind = kind;
        _manage = manage;
        NameLabel.Text = camera.Name;
        EditButton.Visibility = DeleteButton.Visibility = MotionButton.Visibility = manage ? Visibility.Visible : Visibility.Collapsed;
        PlaceActions();
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

        var menu = new ContextMenu();
        MenuItem Item(string header, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            menu.Items.Add(item);
            return item;
        }
        if (manage) Item("Pantalla completa", (_, _) => FullscreenRequested?.Invoke(this));
        _snapshotItem = Item("Captura", Snapshot_Click);
        _recordItem = Item("Grabar", Record_Click);
        if (manage)
        {
            Item("Editar…", Edit_Click);
            Item("Duplicar…", (_, _) => DuplicateRequested?.Invoke(this));
            // Not IsCheckable: the check mark is set by the owner (SetMotionEnabled); a click only asks for the toggle.
            _motionItem = Item("Detección de movimiento", Motion_Click);
        }
        // Shown only while the tile is zoomed.
        ResetZoomItem = Item("Restablecer zoom", (_, _) => ResetZoom());
        ResetZoomItem.Visibility = Visibility.Collapsed;
        if (manage)
        {
            menu.Items.Add(new Separator());
            Item("Eliminar", Delete_Click);
        }
        ContextMenu = menu;

        if (kind == StreamKind.Sub && acquireShared is not null)
        {
            _lease = acquireShared(camera);
            _session = _lease?.Session;
        }
        else
        {
            try
            {
                _session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
            }
            catch (InvalidOperationException)
            {
                _session = null;
            }
        }
        if (_session is null)
        {
            StatusLabel.Text = "Falta la URL RTSP";
            Video.Opacity = 0.4;
            SnapshotButton.IsEnabled = RecordButton.IsEnabled = false;
            _snapshotItem.IsEnabled = _recordItem.IsEnabled = false;
            return;
        }
        _onState = state => Dispatcher.BeginInvoke(() => ShowState(state));
        _onError = error => Dispatcher.BeginInvoke(() => ShowError(error));
        _onInfo = _ => Dispatcher.BeginInvoke(UpdateAudioButton);
        // A failure reported after the tile was silenced (or removed) is stale: nothing to release or report.
        _onAudioFailed = message => Dispatcher.BeginInvoke(() => { if (!_disposed && AudioOn) AudioFailed?.Invoke(this, message); });
        _session.StateChanged += _onState;
        _session.ErrorOccurred += _onError;
        _session.InfoAvailable += _onInfo;
        _session.AudioFailed += _onAudioFailed;
        SizeChanged += (_, _) =>
        {
            if (_zoom.IsZoomed) ApplyZoom(); // re-clamps to the new size and updates the decode box
            else UpdateTargetSize();
        };
        // The letterbox offset of the image changes with the frame's aspect ratio.
        Video.SizeChanged += (_, _) =>
        {
            if (_zoom.IsZoomed) ApplyZoom();
        };
        CompositionTarget.Rendering += OnRendering;
        if (_lease is null)
        {
            ShowState(SessionState.Connecting);
            _session.Start();
            return;
        }
        // A shared session may already be playing, retrying or failed. Show where it is once the owner has
        // subscribed to this tile's events (StreamFailed must reach it), reading the state then: changes queued
        // before that run first, later ones after. Its errors were already reported by the window.
        ShowState(SessionState.Connecting);
        var session = _session;
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            var current = session.State;
            if (current != SessionState.Playing && session.LastErrorKind is { } lastError) ShowErrorKind(lastError);
            ShowState(current == SessionState.Idle ? SessionState.Connecting : current);
            UpdateAudioButton();
        });
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }
    /// <summary>False when the camera has no usable URL, so the tile will never show video.</summary>
    public bool HasStream => _session is not null;

    /// <summary>
    /// Big (main-stream) tiles get large buttons bottom-centre in a dark pill, clear of the stats and zoom
    /// labels in the bottom corners; thumbnails keep the small ones in the top-right corner.
    /// </summary>
    void PlaceActions()
    {
        if (Kind != StreamKind.Main) return;
        Actions.HorizontalAlignment = HorizontalAlignment.Center;
        Actions.VerticalAlignment = VerticalAlignment.Bottom;
        Actions.Margin = new Thickness(0, 0, 0, 24);
        Actions.Padding = new Thickness(6);
        Actions.CornerRadius = new CornerRadius(10);
        Actions.Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
        var large = (Style)FindResource("TileButtonLarge");
        foreach (var button in ActionButtons.Children.OfType<Button>()) button.Style = large;
    }

    public event Action<CameraTile>? EditRequested;
    public event Action<CameraTile>? DuplicateRequested;
    public event Action<CameraTile>? DeleteRequested;
    public event Action<CameraTile>? RecordRequested;
    public event Action<string, string?>? Notify;

    const string DragFormat = "Centinela.CameraId";
    static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    static readonly Brush DropBorder = Brushes.DodgerBlue;
    static readonly Brush MotionBorder = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    bool _dropHover;
    bool _motionActive;
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
    readonly MenuItem _snapshotItem = null!, _recordItem = null!;
    // Manage tiles only.
    readonly MenuItem? _motionItem;
    /// <summary>"Restablecer zoom" in the context menu: collapsed until zoom support shows it.</summary>
    internal MenuItem ResetZoomItem { get; private set; } = null!;

    [DllImport("user32.dll")]
    static extern uint GetDoubleClickTime();

    static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    readonly ZoomState _zoom = new();
    readonly MatrixTransform _zoomTransform = new();
    // Last mouse position of an ongoing pan (left button held on a zoomed tile).
    Point? _panLast;

    /// <summary>Wheel zoom and drag-to-pan; set by the owner (big tiles of the featured/dual views and fullscreen).</summary>
    public bool EnableZoom { get; set; }

    /// <summary>True while the image is zoomed in.</summary>
    public bool IsZoomed => _zoom.IsZoomed;

    /// <summary>Back to the whole image.</summary>
    public void ResetZoom()
    {
        _zoom.Reset();
        ApplyZoom();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!EnableZoom || _session is null || _disposed)
        {
            base.OnMouseWheel(e);
            return;
        }
        SyncZoomView();
        var p = e.GetPosition(VideoHost);
        _zoom.WheelAt(p.X, p.Y, e.Delta / 120.0);
        ApplyZoom();
        e.Handled = true;
    }

    /// <summary>
    /// Gives the zoom the host size and the image's letterboxed rectangle inside it (layout offset, which
    /// excludes the render transform), so panning never brings the black bars into view.
    /// </summary>
    Vector SyncZoomView()
    {
        var o = VisualTreeHelper.GetOffset(Video);
        _zoom.Resize(VideoHost.ActualWidth, VideoHost.ActualHeight);
        _zoom.SetContent(o.X, o.Y, Video.ActualWidth, Video.ActualHeight);
        return o;
    }

    /// <summary>Shows the zoom state: transform, scaling quality, label, menu item and decode size.</summary>
    void ApplyZoom()
    {
        var o = SyncZoomView();
        if (_zoom.IsZoomed)
        {
            // The zoom lives in VideoHost coordinates; the Image sits inside it at its letterbox offset o, so
            // host = o + M(local) must equal Scale·(o + local) + Offset, i.e. M's translation is (Scale − 1)·o + Offset.
            var s = _zoom.Scale;
            // One transform per tile, updated in place: panning must not allocate on every mouse move.
            _zoomTransform.Matrix = new Matrix(s, 0, 0, s, (s - 1) * o.X + _zoom.OffsetX, (s - 1) * o.Y + _zoom.OffsetY);
            if (!ReferenceEquals(Video.RenderTransform, _zoomTransform)) Video.RenderTransform = _zoomTransform;
        }
        else
        {
            Video.RenderTransform = Transform.Identity;
        }
        RenderOptions.SetBitmapScalingMode(Video, _zoom.IsZoomed ? BitmapScalingMode.HighQuality : BitmapScalingMode.Linear);
        ZoomBorder.Visibility = _zoom.IsZoomed ? Visibility.Visible : Visibility.Collapsed;
        ZoomLabel.Text = string.Format(Spanish, "{0:0.#}×", _zoom.Scale);
        ResetZoomItem.Visibility = _zoom.IsZoomed ? Visibility.Visible : Visibility.Collapsed;
        UpdateTargetSize();
    }

    void EndPan()
    {
        if (_panLast is null) return;
        _panLast = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _panLast = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            _dragStart = null;
            EndPan();
            FullscreenRequested?.Invoke(this);
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        if (_zoom.IsZoomed)
        {
            _panLast = _dragStart;
            CaptureMouse();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndPan();
        // _dragStart is cleared when a drag starts or on double-click, so reaching here with it set means a plain click.
        if (_dragStart is null) return;
        _dragStart = null;
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panLast is { } last)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndPan();
                return;
            }
            var p = e.GetPosition(this);
            _zoom.Pan(p.X - last.X, p.Y - last.Y);
            _panLast = p;
            ApplyZoom();
            // A pan is not a click; zoomed tiles never start a reorder drag.
            if (_dragStart is { } origin
                && (Math.Abs(p.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
                    || Math.Abs(p.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance))
            {
                _dragStart = null;
                _clickTimer.Stop();
            }
            return;
        }
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
        if (_manage && e.Data.GetDataPresent(DragFormat))
        {
            _dropHover = true;
            UpdateBorder();
        }
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dropHover = false;
        UpdateBorder();
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = _manage && e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        _dropHover = false;
        UpdateBorder();
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
        // Not laid out yet (or collapsed): a 0×0 request would mean "native size" and enlarge a shared session's
        // decode for every lease; keep the last size until there is a real one.
        if (ActualWidth < 1 || ActualHeight < 1) return;
        // Zoomed images are decoded larger (FrameGeometry never upscales beyond the native size).
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)(ActualWidth * dpi.DpiScaleX * _zoom.Scale);
        var height = (int)(ActualHeight * dpi.DpiScaleY * _zoom.Scale);
        // A shared session decodes at the largest size any of its leases asks for.
        if (_lease is not null) _lease.SetTargetSize(width, height);
        else _session?.SetTargetSize(width, height);
    }

    void ShowError(StreamError error)
    {
        if (_disposed) return;
        ShowErrorKind(error.Kind);
        // A shared session's errors are reported once by the window, not by each tile that shows it.
        if (_lease is null) ErrorReported?.Invoke(this, error);
    }

    void ShowErrorKind(StreamErrorKind kind)
    {
        _errorKind = kind;
        var text = ErrorCenter.Translate(Camera, kind);
        StatusLabel.Text = kind == StreamErrorKind.AuthFailed ? text.Short : $"{text.Short} · reintentando";
        FixButton.Visibility = kind == StreamErrorKind.AuthFailed && _manage ? Visibility.Visible : Visibility.Collapsed;
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
            if (_lease is null) PlayingReached?.Invoke(this);
        }
    }

    void Edit_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this);

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this);

    async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        var path = AppPaths.SnapshotFile(Camera.Name, DateTime.Now);
        SnapshotButton.IsEnabled = _snapshotItem.IsEnabled = false;
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
            SnapshotButton.IsEnabled = _snapshotItem.IsEnabled = true;
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
            _recordItem.Header = "Grabar";
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
        _recordItem.Header = "Detener grabación";
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

    /// <summary>Raised when the user asks to turn this camera's motion detection on or off (manage tiles only).</summary>
    public event Action<CameraTile>? MotionToggleRequested;

    void Motion_Click(object sender, RoutedEventArgs e) => MotionToggleRequested?.Invoke(this);

    /// <summary>Red frame while the camera has a motion event in progress.</summary>
    public void SetMotionActive(bool active)
    {
        _motionActive = active;
        UpdateBorder();
    }

    /// <summary>Shows whether detection is on: button tooltip, menu check and the crossed-out eye by the name.</summary>
    public void SetMotionEnabled(bool enabled)
    {
        MotionButton.ToolTip = enabled ? "Detección de movimiento: activada" : "Detección de movimiento: desactivada";
        MotionButton.Opacity = enabled ? 1 : 0.6;
        if (_motionItem is not null) _motionItem.IsChecked = enabled;
        MotionOffIndicator.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Drop-target highlight over the motion frame over the normal border.</summary>
    void UpdateBorder()
    {
        if (_dropHover)
        {
            Frame.BorderBrush = DropBorder;
            Frame.BorderThickness = new Thickness(_motionActive ? 3 : 1);
        }
        else if (_motionActive)
        {
            Frame.BorderBrush = MotionBorder;
            Frame.BorderThickness = new Thickness(3);
        }
        else
        {
            Frame.BorderBrush = NormalBorder;
            Frame.BorderThickness = new Thickness(1);
        }
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

    /// <summary>Set by the owner for big (Main) tiles and fullscreen; the button shows only if the stream has audio.</summary>
    public bool AudioCapable { get; set; }
    public bool AudioOn { get; private set; }
    public event Action<CameraTile>? AudioToggleRequested;
    /// <summary>Raised on the UI thread when a new connection has no audio track while this tile's audio is on.</summary>
    public event Action<CameraTile>? AudioLost;
    /// <summary>Raised on the UI thread when the audio of this tile, while on, cannot be played.</summary>
    public event Action<CameraTile, string>? AudioFailed;

    void Audio_Click(object sender, RoutedEventArgs e) => AudioToggleRequested?.Invoke(this);

    /// <summary>Plays this tile's audio into <paramref name="sink"/>, or silences it with null.</summary>
    public void SetAudio(IAudioSink? sink)
    {
        // Shared sessions never play audio (Sub tiles are not audio-capable): the sink would reach every viewer.
        AudioOn = sink is not null && !_disposed && _lease is null;
        if (_lease is null) _session?.SetAudioSink(AudioOn ? sink : null);
        AudioButton.Content = AudioOn ? "🔊" : "🔇";
        // A disabled button keeps the tooltip that explains why.
        if (AudioButton.IsEnabled) AudioButton.ToolTip = AudioOn ? "Silenciar" : "Activar sonido";
        AudioIndicator.Visibility = AudioOn ? Visibility.Visible : Visibility.Collapsed;
    }

    public void DisableAudio(string reason)
    {
        AudioButton.IsEnabled = false;
        AudioButton.ToolTip = reason;
    }

    void UpdateAudioButton()
    {
        if (_disposed) return;
        var hasAudio = _session?.Info?.AudioCodec is not null;
        AudioButton.Visibility = AudioCapable && hasAudio ? Visibility.Visible : Visibility.Collapsed;
        // Reconnected to a stream without audio: nothing will sound, so the owner returns the tile to muted.
        if (AudioOn && !hasAudio) AudioLost?.Invoke(this);
    }

    /// <summary>Asks the own session to stop; a shared session is only stopped by releasing its last lease.</summary>
    public void RequestStop()
    {
        if (_lease is null) _session?.RequestStop();
    }

    /// <summary>
    /// Stops the live view and disposes its session off the UI thread, or releases its shared lease (recordings are
    /// owned by RecordingController). Call on the UI thread; repeated calls return the same task.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        _disposed = true;
        if (_lease is null) _session?.SetAudioSink(null);
        AudioOn = false;
        _clickTimer.Stop();
        EndPan();
        _statsTimer?.Stop();
        CompositionTarget.Rendering -= OnRendering;
        var session = _session;
        if (session is not null)
        {
            session.StateChanged -= _onState;
            session.ErrorOccurred -= _onError;
            session.InfoAvailable -= _onInfo;
            session.AudioFailed -= _onAudioFailed;
        }
        if (_lease is not null)
        {
            // Completes once the session is disposed if this was its last lease, at once otherwise.
            _shutdown = _lease.ReleaseAsync();
            return _shutdown;
        }
        session?.RequestStop();
        _shutdown = session is null ? Task.CompletedTask : Task.Run(session.Dispose);
        return _shutdown;
    }

    /// <summary>Starts <see cref="ShutdownAsync"/> without waiting for it.</summary>
    public void Dispose() => _ = ShutdownAsync();
}
