using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public sealed partial class CameraTile : UserControl, IDisposable
{
    readonly StreamSession _session;
    readonly bool _manage;
    StreamSession? _recording;
    // Every stopped recording still being finalized (UI thread only).
    Task _finalizing = Task.CompletedTask;
    Task? _shutdown;
    WriteableBitmap? _bitmap;
    long _frameSequence;
    bool _disposed;

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

        _session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
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

    void OnRendering(object? sender, EventArgs e) =>
        _session.Mailbox.TryRead(ref _frameSequence, frame =>
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
        _session.SetTargetSize((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
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

    /// <summary>Grid tiles show the substream; grab one full-resolution frame from the mainstream instead.</summary>
    async Task<bool> SaveMainStreamSnapshotAsync(string path)
    {
        if (Kind == StreamKind.Main) return await _session.SaveSnapshotAsync(path);
        var main = new StreamSession(StreamUrlBuilder.Build(Camera, StreamKind.Main), Camera.UseUdp);
        try
        {
            main.SetTargetSize(2, 2);
            main.Start();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !_disposed)
            {
                if (main.Mailbox.Sequence > 0) return await main.SaveSnapshotAsync(path);
                await Task.Delay(100);
            }
            return !_disposed && await _session.SaveSnapshotAsync(path);
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
        recording.StateChanged += state =>
        {
            if (state != SessionState.AuthFailed) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_recording != recording) return;
                StopRecording();
                Notify?.Invoke($"No se pudo grabar {name}: credenciales incorrectas.", null);
            });
        };
        _recording = recording;
        recording.Start();
        RecDot.Visibility = Visibility.Visible;
        RecordButton.Content = "⏹";
        RecordButton.ToolTip = "Detener grabación";
        Notify?.Invoke($"Grabando {name}…", null);
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

    public void RequestStop() => _session.RequestStop();

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

        session.RequestStop();
        recording?.RequestStop();
        _shutdown = Task.Run(async () =>
        {
            try
            {
                if (recording is not null) await FinalizeRecordingAsync(recording).ConfigureAwait(false);
                await finalizing.ConfigureAwait(false);
            }
            finally
            {
                session.Dispose();
            }
        });
        return _shutdown;
    }

    /// <summary>Starts <see cref="ShutdownAsync"/> without waiting for it.</summary>
    public void Dispose() => _ = ShutdownAsync();
}
