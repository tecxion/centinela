using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Centinela.Core;
using Centinela.Media;

namespace Centinela.App;

public partial class AddCameraDialog : Window
{
    readonly Camera _camera;
    // Host/port the override URLs currently point at; moved along when the user edits IP or port.
    string _urlHost;
    int _urlPort;
    StreamSession? _test;        // substream: decoded, shown in the preview
    StreamSession? _testMain;    // mainstream: not decoded, only for its info
    readonly DispatcherTimer _testTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    string _mainLine = "", _subLine = "";
    WriteableBitmap? _previewBitmap;
    long _previewSequence;

    public AddCameraDialog(Camera? initial = null, bool isNew = true)
    {
        InitializeComponent();
        _camera = initial?.Clone() ?? new Camera { Brand = Brand.Tapo };
        _urlHost = _camera.Host;
        _urlPort = _camera.Port;
        Title = isNew ? "Añadir cámara" : "Editar cámara";

        NameBox.Text = _camera.Name;
        HostBox.Text = _camera.Host;
        PortBox.Text = _camera.Port.ToString();
        UserBox.Text = _camera.User;
        PasswordInput.Password = _camera.Password;
        MainUrlBox.Text = _camera.MainUrlOverride ?? "";
        SubUrlBox.Text = _camera.SubUrlOverride ?? "";
        UdpBox.IsChecked = _camera.UseUdp;
        BrandBox.SelectedIndex = (int)_camera.Brand;

        _testTimer.Tick += (_, _) => FinishTest();
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            StopTest();
        };
    }

    public Camera Result => _camera;

    void Brand_Changed(object sender, SelectionChangedEventArgs e)
    {
        var brand = (Brand)BrandBox.SelectedIndex;
        if (brand == Brand.Imou && UserBox.Text.Length == 0) UserBox.Text = "admin";
        if (brand == Brand.Custom) AdvancedBox.IsExpanded = true;
        HintText.Text = brand switch
        {
            Brand.Tapo => "Usa el usuario y la contraseña de la «Cuenta de cámara» (app Tapo › Ajustes avanzados).",
            Brand.Imou => "Usuario «admin». La contraseña es el código de seguridad de la pegatina. Si no conecta, activa RTSP/ONVIF en la app Imou.",
            _ => "Pon la URL RTSP en «Avanzado» sin usuario ni contraseña (rtsp://IP:puerto/ruta). Escribe las credenciales en los campos Usuario y Contraseña: se guardan cifradas.",
        };
    }

    bool TryReadForm(out string error)
    {
        var brand = (Brand)BrandBox.SelectedIndex;
        var mainUrl = MainUrlBox.Text.Trim();
        error = "";
        if (NameBox.Text.Trim().Length == 0) error = "Pon un nombre a la cámara.";
        else if (brand != Brand.Custom && HostBox.Text.Trim().Length == 0) error = "La IP es obligatoria.";
        else if (brand == Brand.Custom && mainUrl.Length == 0) error = "La URL principal es obligatoria.";
        else if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535) error = "Puerto no válido.";
        if (error.Length > 0) return false;

        var host = HostBox.Text.Trim();
        var newPort = int.Parse(PortBox.Text);
        if (!string.Equals(host, _urlHost, StringComparison.OrdinalIgnoreCase) || newPort != _urlPort)
        {
            // ONVIF-added cameras keep their stream URLs as overrides: follow the IP/port change.
            MainUrlBox.Text = mainUrl = StreamUrlBuilder.RebaseOverride(mainUrl, _urlHost, _urlPort, host, newPort);
            SubUrlBox.Text = StreamUrlBuilder.RebaseOverride(SubUrlBox.Text.Trim(), _urlHost, _urlPort, host, newPort);
            _urlHost = host;
            _urlPort = newPort;
        }

        _camera.Brand = brand;
        _camera.Name = NameBox.Text.Trim();
        _camera.Host = host;
        _camera.Port = newPort;
        _camera.User = UserBox.Text.Trim();
        _camera.Password = PasswordInput.Password;
        _camera.MainUrlOverride = mainUrl.Length == 0 ? null : mainUrl;
        _camera.SubUrlOverride = SubUrlBox.Text.Trim() is { Length: > 0 } sub ? sub : null;
        _camera.UseUdp = UdpBox.IsChecked == true;
        return true;
    }

    void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            TestStatus.Text = error;
            return;
        }
        StopTest();
        _mainLine = "Principal: conectando…";
        _subLine = "Secundaria: conectando…";
        ShowTestLines();
        TestStatus.Text = "Conectando…";
        _test = StartTestSession(StreamKind.Sub, decode: true, line => _subLine = line, "Secundaria");
        _testMain = StartTestSession(StreamKind.Main, decode: false, line => _mainLine = line, "Principal");
        _testTimer.Start();
    }

    StreamSession StartTestSession(StreamKind kind, bool decode, Action<string> setLine, string label)
    {
        var session = new StreamSession(StreamUrlBuilder.Build(_camera, kind), _camera.UseUdp, decode);
        if (decode) session.SetTargetSize(640, 360);
        // Callbacks arrive on the session thread; ignored once the test was replaced, finished or the dialog closed.
        session.InfoAvailable += info => Dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrentTest(session)) return;
            setLine($"{label}: {info.Describe()}");
            if (decode) TestStatus.Text = "";
            ShowTestLines();
        });
        session.ErrorOccurred += err => Dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrentTest(session)) return;
            // Never err.Detail: only the translated, user-facing text.
            setLine($"{label}: {ErrorCenter.Translate(_camera, err.Kind).Short}");
            if (decode) TestStatus.Text = "";
            ShowTestLines();
        });
        session.Start();
        return session;
    }

    // The timer runs only while a test is in progress: FinishTest keeps _test for the preview, but its results are final.
    bool IsCurrentTest(StreamSession session) => _testTimer.IsEnabled && (_test == session || _testMain == session);

    void ShowTestLines() => TestResult.Text = $"{_mainLine}\n{_subLine}";

    /// <summary>After 10 s: stop both sessions (the preview keeps its last frame) and mark what never answered.</summary>
    void FinishTest()
    {
        _testTimer.Stop();
        if (_mainLine.EndsWith("conectando…")) _mainLine = "Principal: sin respuesta en 10 s";
        if (_subLine.EndsWith("conectando…")) _subLine = "Secundaria: sin respuesta en 10 s";
        ShowTestLines();
        if (TestStatus.Text == "Conectando…") TestStatus.Text = "";
        // Stopping joins the session thread: keep that off the UI thread.
        foreach (var s in new[] { _test, _testMain })
            if (s is not null) _ = Task.Run(s.Dispose);
        _testMain = null;
        // _test stays referenced so OnRendering keeps showing its last frame; it is already stopping.
    }

    void OnRendering(object? sender, EventArgs e) =>
        _test?.Mailbox.TryRead(ref _previewSequence, frame =>
        {
            if (_previewBitmap is null || _previewBitmap.PixelWidth != frame.Width || _previewBitmap.PixelHeight != frame.Height)
            {
                _previewBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                Preview.Source = _previewBitmap;
            }
            _previewBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Data, frame.Stride, 0);
        });

    void StopTest()
    {
        _testTimer.Stop();
        // Stopping joins the session thread: keep that off the UI thread (a second Dispose is a no-op).
        foreach (var s in new[] { _test, _testMain })
            if (s is not null) _ = Task.Run(s.Dispose);
        _test = _testMain = null;
        _previewSequence = 0;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            HintText.Text = error;
            return;
        }
        DialogResult = true;
    }
}
