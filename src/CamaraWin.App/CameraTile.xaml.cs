using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public sealed partial class CameraTile : UserControl, IDisposable
{
    // Null when the camera has no usable URL (custom camera without one): the tile only shows the error.
    readonly StreamSession? _session;
    readonly bool _manage;
    StreamSession? _recording;
    // Every stopped recording still being finalized (UI thread only).
    Task _finalizing = Task.CompletedTask;
    Task? _shutdown;
    WriteableBitmap? _bitmap;
    long _frameSequence;
    bool _disposed;
    bool _recordingPauseNotified;

    public CameraTile(Camera camera, StreamKind kind, bool manage = true)
    {
        InitializeComponent();
        Camera = camera;
        Kind = kind;
        _manage = manage;
        NameLabel.Text = camera.Name;
        EditButton.Visibility = DeleteButton.Visibility = manage ? Visibility.Visible : Visibility.Collapsed;
        MouseEnter += (_, _) => Actions.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => Actions.Visibility = Visibility.Collapsed;

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
        SizeChanged += (_, _) => UpdateTargetSize();
        CompositionTarget.Rendering += OnRendering;
        ShowState(SessionState.Connecting);
        _session.Start();
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }

    public event Action<CameraTile>? EditRequested;
    public event Action<CameraTile>? DeleteRequested;
    public event Action<string, string?>? Notify;

    const string DragFormat = "CamaraWin.CameraId";
    static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    static readonly Brush DropBorder = Brushes.DodgerBlue;
    Point? _dragStart;

    public event Action<CameraTile>? FullscreenRequested;
    public event Action<Guid, Guid>? SwapRequested;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _dragStart = null;
            FullscreenRequested?.Invoke(this);
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_manage || _dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
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

    void OnRendering(object? sender, EventArgs e) =>
        _session?.Mailbox.TryRead(ref _frameSequence, frame =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                Video.Source = _bitmap;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Data, frame.Stride, 0);
        });

    void UpdateTargetSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _session?.SetTargetSize((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
    }

    void ShowState(SessionState state)
    {
        if (_disposed) return;
        StatusLabel.Text = state switch
        {
            SessionState.Connecting => "Conectando…",
            SessionState.Reconnecting => "Reconectando…",
            SessionState.AuthFailed => "Credenciales incorrectas",
            SessionState.Stopped => "Detenida",
            _ => "",
        };
        Video.Opacity = state == SessionState.Playing ? 1 : 0.4;
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

    public bool IsRecording => _recording is not null;

    void Record_Click(object sender, RoutedEventArgs e)
    {
        var name = Camera.Name;
        if (_recording is not null)
        {
            StopRecording().ContinueWith(finalize => Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                if (finalize.Exception is { } ex)
                    Notify?.Invoke($"No se pudo cerrar la grabación de {name}: {ex.GetBaseException().Message}", null);
                else
                    Notify?.Invoke($"Grabación guardada en {AppPaths.RecordingsDirectory}", AppPaths.RecordingsDirectory);
            }), TaskScheduler.Default);
            Notify?.Invoke($"Guardando grabación de {name}…", null);
            return;
        }
        var recording = new StreamSession(StreamUrlBuilder.Build(Camera, StreamKind.Main), Camera.UseUdp, decode: false);
        recording.StartRecording(() => AppPaths.RecordingFile(name, DateTime.Now));
        recording.RecordingFailed += message => Dispatcher.BeginInvoke(() =>
        {
            if (_recording != recording) return;
            StopRecording();
            Notify?.Invoke($"Grabación de {name} detenida: {message}", null);
        });
        recording.StateChanged += state => Dispatcher.BeginInvoke(() =>
        {
            if (_recording != recording || _disposed) return;
            if (state == SessionState.AuthFailed)
            {
                StopRecording();
                Notify?.Invoke($"No se pudo grabar {name}: credenciales incorrectas.", null);
                return;
            }
            ShowRecordingState(state, name);
        });
        _recording = recording;
        _recordingPauseNotified = false;
        recording.Start();
        ShowRecordingDot(paused: true, "Grabación: conectando…");
        RecordButton.Content = "⏹";
        RecordButton.ToolTip = "Detener grabación";
        Notify?.Invoke($"Grabando {name}…", null);
    }

    /// <summary>
    /// Solid red while the recording session is playing (packets reach the file); hollow while it
    /// (re)connects, with one status notice per interruption.
    /// </summary>
    void ShowRecordingState(SessionState state, string name)
    {
        switch (state)
        {
            case SessionState.Playing:
                _recordingPauseNotified = false;
                ShowRecordingDot(paused: false, "Grabando");
                break;
            case SessionState.Reconnecting:
                ShowRecordingDot(paused: true, "Grabación en pausa: reconectando…");
                if (!_recordingPauseNotified)
                {
                    _recordingPauseNotified = true;
                    Notify?.Invoke($"Grabación de {name} en pausa: reconectando…", null);
                }
                break;
            case SessionState.Connecting:
                // Connecting follows Reconnecting on every retry; before the first Playing it is the initial connection.
                ShowRecordingDot(paused: true, _recordingPauseNotified ? "Grabación en pausa: reconectando…" : "Grabación: conectando…");
                break;
        }
    }

    void ShowRecordingDot(bool paused, string tooltip)
    {
        RecDot.Visibility = Visibility.Visible;
        RecDot.Fill = paused ? Brushes.Transparent : Brushes.Red;
        RecDot.Stroke = Brushes.Red;
        RecDot.StrokeThickness = paused ? 2 : 0;
        RecDot.Opacity = paused ? 0.7 : 1;
        RecDot.ToolTip = tooltip;
    }

    /// <summary>Stops the active recording; the returned task (also tracked for shutdown) completes once the file is finalized.</summary>
    Task StopRecording()
    {
        var recording = _recording;
        if (recording is null) return Task.CompletedTask;
        _recording = null;
        RecDot.Visibility = Visibility.Collapsed;
        RecordButton.Content = "⏺";
        RecordButton.ToolTip = "Grabar";
        var finalize = Task.Run(() => FinalizeRecordingAsync(recording));
        _finalizing = Task.WhenAll(_finalizing, finalize);
        return finalize;
    }

    static async Task FinalizeRecordingAsync(StreamSession recording)
    {
        try
        {
            await recording.StopRecordingAsync().ConfigureAwait(false);
        }
        finally
        {
            recording.Dispose();
        }
    }

    public void RequestStop() => _session?.RequestStop();

    /// <summary>
    /// Stops the live view and any recording, then finalizes recordings (active and already stopped)
    /// and disposes the sessions off the UI thread. Call on the UI thread; repeated calls return the same task.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        var session = _session;
        var recording = _recording;
        _recording = null;
        var finalizing = _finalizing;

        session?.RequestStop();
        recording?.RequestStop();
        _shutdown = Task.Run(async () =>
        {
            try
            {
                // WhenAll: a failing active finalize must not skip waiting for the earlier ones.
                var finalizeActive = recording is null ? Task.CompletedTask : FinalizeRecordingAsync(recording);
                await Task.WhenAll(finalizeActive, finalizing).ConfigureAwait(false);
            }
            finally
            {
                session?.Dispose();
            }
        });
        return _shutdown;
    }

    /// <summary>Starts <see cref="ShutdownAsync"/> without waiting for it.</summary>
    public void Dispose() => _ = ShutdownAsync();
}
