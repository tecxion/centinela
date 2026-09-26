using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public partial class AddCameraDialog : Window
{
    readonly Camera _camera;
    StreamSession? _test;
    WriteableBitmap? _previewBitmap;
    long _previewSequence;

    public AddCameraDialog(Camera? initial = null, bool isNew = true)
    {
        InitializeComponent();
        _camera = initial?.Clone() ?? new Camera { Brand = Brand.Tapo };
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
            _ => "Pon la URL RTSP completa en «Avanzado». El usuario y la contraseña se añaden si la URL no los incluye.",
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

        _camera.Brand = brand;
        _camera.Name = NameBox.Text.Trim();
        _camera.Host = HostBox.Text.Trim();
        _camera.Port = int.Parse(PortBox.Text);
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
        var session = new StreamSession(StreamUrlBuilder.Build(_camera, StreamKind.Sub), _camera.UseUdp);
        session.SetTargetSize(640, 360);
        session.StateChanged += state => Dispatcher.BeginInvoke(() =>
        {
            if (_test != session) return;
            TestStatus.Text = state switch
            {
                SessionState.Connecting => "Conectando…",
                SessionState.Reconnecting => $"No conecta: {session.LastError}",
                SessionState.AuthFailed => "Usuario o contraseña incorrectos.",
                _ => "",
            };
        });
        _test = session;
        session.Start();
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
        // Stopping joins the session thread: keep that off the UI thread.
        if (_test is { } test) _ = Task.Run(test.Dispose);
        _test = null;
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
