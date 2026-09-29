using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Windows;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Media;

namespace Centinela.App;

/// <summary>
/// «Calidad de la cámara…»: a read-only report of the camera's encoder settings and what it accepts, through
/// ONVIF and the Dahua/Imou HTTP API. Nothing is written to the camera. The report never contains the password.
/// </summary>
public partial class CameraQualityWindow : Window
{
    readonly Camera _camera;
    CancellationTokenSource? _query;

    public CameraQualityWindow(Camera camera)
    {
        InitializeComponent();
        _camera = camera;
        Title = $"Calidad de la cámara — {camera.Name}";
        Loaded += (_, _) => _ = QueryAsync();
        Closed += (_, _) => _query?.Cancel();
    }

    async Task QueryAsync()
    {
        _query?.Cancel();
        var query = _query = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        RetryButton.IsEnabled = false;
        var report = new StringBuilder();
        void Line(string text = "")
        {
            report.AppendLine(text);
            if (!query.IsCancellationRequested) Report.Text = report.ToString();
        }

        var host = HostOf(_camera);
        Line($"Cámara: {_camera.Name} ({_camera.Brand}) · {host}");
        Line($"Usuario: {(_camera.User.Length > 0 ? _camera.User : "(ninguno)")}");
        Line();
        if (host.Length == 0)
        {
            Line("Esta cámara no tiene dirección de red: no se puede consultar.");
            RetryButton.IsEnabled = true;
            return;
        }

        var baseUrl = new Uri($"http://{host}/");
        var deviceUrl = new Uri(baseUrl, "/onvif/device_service");
        using var http = OnvifClient.CreateHttpClient(deviceUrl, _camera.User, _camera.Password);

        Line("── ONVIF ──");
        try
        {
            var client = new OnvifClient(http, deviceUrl, _camera.User, _camera.Password);
            try
            {
                var info = await client.GetDeviceInformationAsync(query.Token);
                Line($"Dispositivo: {info.Manufacturer} {info.Model}");
            }
            catch (OnvifException ex) when (ex is not OnvifAuthException)
            {
                Line($"Dispositivo: no responde ({ex.Message})");
            }
            var configurations = await client.GetEncoderSettingsAsync(query.Token);
            if (configurations.Count == 0) Line("No informa de ninguna configuración de vídeo.");
            foreach (var (token, settings, options) in configurations)
            {
                Line(Describe(settings) + $"  [token {token}]");
                if (options is null) Line("    Admite: (no lo informa)");
                else
                {
                    Line($"    Admite fps {Range(options.FpsMin, options.FpsMax)} · bitrate {Range(options.BitrateMinKbps, options.BitrateMaxKbps)} kbps");
                    if (options.Resolutions.Count > 0)
                        Line("    Resoluciones: " + string.Join(", ", options.Resolutions.Select(r => $"{r.Width}×{r.Height}")));
                }
            }
        }
        catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Line($"No se pudo consultar: {Explain(ex)}");
        }
        Line();

        Line("── API Dahua/Imou ──");
        try
        {
            var streams = await DahuaApi.GetEncodeAsync(http, baseUrl, query.Token);
            if (streams.Count == 0) Line("Responde, pero sin ajustes de vídeo reconocibles.");
            foreach (var stream in streams) Line(Describe(stream));
            try
            {
                var caps = await DahuaApi.GetEncodeCapsAsync(http, baseUrl, query.Token);
                if (caps.Count > 0)
                {
                    Line("Capacidades:");
                    foreach (var cap in caps.Take(60)) Line("    " + cap);
                }
            }
            catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException)
            {
                Line($"Capacidades: no las informa ({Explain(ex)})");
            }
        }
        catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException)
        {
            Line($"No se pudo consultar: {Explain(ex)}");
        }
        Line();
        Line("No se ha cambiado nada en la cámara.");
        RetryButton.IsEnabled = true;
    }

    static string HostOf(Camera camera)
    {
        if (camera.Host.Trim().Length > 0) return camera.Host.Trim();
        return Uri.TryCreate(camera.MainUrlOverride ?? camera.SubUrlOverride, UriKind.Absolute, out var url) ? url.Host : "";
    }

    static string Describe(EncoderSettings s)
    {
        var parts = new List<string> { s.Name.Length > 0 ? s.Name : "(sin nombre)" };
        if (s.Codec.Length > 0) parts.Add(s.Codec);
        if (s.Width > 0) parts.Add($"{s.Width}×{s.Height}");
        if (s.Fps > 0) parts.Add($"{s.Fps} fps");
        if (s.BitrateKbps > 0) parts.Add($"{s.BitrateKbps} kbps" + (s.RateControl is { Length: > 0 } rc ? $" {rc}" : ""));
        if (s.Gop > 0) parts.Add($"GOP {s.Gop}");
        return string.Join(" · ", parts);
    }

    static string Range(int min, int max) =>
        max <= 0 ? "?" : string.Create(CultureInfo.InvariantCulture, $"{min}–{max}");

    static string Explain(Exception ex) => ex switch
    {
        TaskCanceledException => "no contesta (tiempo agotado)",
        HttpRequestException http => CredentialSanitizer.Sanitize(http.Message),
        _ => CredentialSanitizer.Sanitize(ex.Message),
    };

    void Retry_Click(object sender, RoutedEventArgs e) => _ = QueryAsync();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Report.Text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: the user can try again */ }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
