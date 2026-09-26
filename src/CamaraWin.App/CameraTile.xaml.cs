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
    WriteableBitmap? _bitmap;
    long _frameSequence;
    bool _disposed;

    public CameraTile(Camera camera, StreamKind kind)
    {
        InitializeComponent();
        Camera = camera;
        Kind = kind;
        NameLabel.Text = camera.Name;

        _session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
        _session.StateChanged += state => Dispatcher.BeginInvoke(() => ShowState(state));
        SizeChanged += (_, _) => UpdateTargetSize();
        CompositionTarget.Rendering += OnRendering;
        ShowState(SessionState.Connecting);
        _session.Start();
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }

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

    public void RequestStop() => _session.RequestStop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        _session.Dispose();
    }
}
