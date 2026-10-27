using System.Globalization;
using System.Net.Http;
using Centinela.Core;
using Centinela.Core.Onvif;

namespace Centinela.Desktop.Services.Quality;

/// <summary>
/// Asks the camera for its encoder settings (ONVIF and the Dahua/Imou HTTP API) and applies changes through ONVIF.
/// The report lines carry the user name but never the password.
/// </summary>
sealed class QualityQuery(Camera camera)
{
    // Generous: a camera on weak Wi-Fi may take seconds per answer, and each authenticated call is two round
    // trips (Digest challenge, then the request again).
    static readonly TimeSpan OnvifTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(8);
    /// <summary>The whole query gives up after this long.</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(120);
    static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>The values found before Centinela's first change (<see cref="EncoderSnapshot"/> text), for the report.</summary>
    public string? Originals { get; set; } = camera.OriginalEncoders;

    string Host => QualityText.HostOf(camera);
    Uri BaseUrl => new($"http://{Host}/");
    Uri DeviceUrl => new(BaseUrl, "/onvif/device_service");

    HttpClient CreateOnvifHttp() => OnvifClient.CreateHttpClient(DeviceUrl, camera.User, camera.Password, OnvifTimeout);

    /// <summary>Writes the report through <paramref name="line"/> and returns the ONVIF configurations (none on failure).</summary>
    public async Task<IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)>> RunAsync(
        bool includeApi, Action<string> line, CancellationToken ct)
    {
        using var query = CancellationTokenSource.CreateLinkedTokenSource(ct);
        query.CancelAfter(QueryTimeout);
        void Line(string text = "") => line(text);

        Line($"Cámara: {camera.Name} ({camera.Brand}) · {Host}");
        Line($"Usuario: {(camera.User.Length > 0 ? camera.User : "(ninguno)")}");
        Line();
        if (Host.Length == 0)
        {
            Line("Esta cámara no tiene dirección de red: no se puede consultar.");
            return [];
        }

        using var http = CreateOnvifHttp();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string Took() => string.Create(Spanish, $"{watch.Elapsed.TotalSeconds:0.0} s");

        Line("── ONVIF ──");
        IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)> configurations = [];
        try
        {
            var client = new OnvifClient(http, DeviceUrl, camera.User, camera.Password);
            watch.Restart();
            try
            {
                var info = await client.GetDeviceInformationAsync(query.Token);
                Line($"Dispositivo: {info.Manufacturer} {info.Model}  ({Took()})");
            }
            catch (OnvifException ex) when (ex is not OnvifAuthException)
            {
                Line($"Dispositivo: no responde ({QualityText.Explain(ex)})");
            }
            watch.Restart();
            configurations = await client.GetEncoderSettingsAsync(query.Token);
            Line($"Ajustes de vídeo leídos en {Took()}:");
            if (configurations.Count == 0) Line("No informa de ninguna configuración de vídeo.");
            foreach (var (token, settings, options) in configurations)
            {
                Line(QualityText.Describe(settings) + $"  [token {token}]");
                if (options is null) Line("    Admite: (no lo informa)");
                else
                {
                    Line($"    Admite fps {QualityText.Range(options.FpsMin, options.FpsMax)} · bitrate {QualityText.Range(options.BitrateMinKbps, options.BitrateMaxKbps)} kbps");
                    if (options.Resolutions.Count > 0)
                        Line("    Resoluciones: " + string.Join(", ", options.Resolutions.Select(r => $"{r.Width}×{r.Height}")));
                }
            }
            if (EncoderSnapshot.Parse(Originals) is { Count: > 0 } originals)
                Line("Originales (antes de cambiarla con Centinela): " + string.Join(" · ",
                    originals.Select(o => $"{o.Key} {o.Value.Width}×{o.Value.Height} {o.Value.Fps} fps {o.Value.BitrateKbps} kbps")));
        }
        catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Line($"No se pudo consultar tras {Took()}: {QualityText.Explain(ex)}");
        }
        Line();

        if (includeApi) await QueryApiAsync(Line, query.Token);
        return configurations;
    }

    async Task QueryApiAsync(Action<string> line, CancellationToken ct)
    {
        line("── API Dahua/Imou ──");
        using var api = OnvifClient.CreateHttpClient(DeviceUrl, camera.User, camera.Password, ApiTimeout);
        try
        {
            var streams = await DahuaApi.GetEncodeAsync(api, BaseUrl, ct);
            if (streams.Count == 0) line("Responde, pero sin ajustes de vídeo reconocibles.");
            foreach (var stream in streams) line(QualityText.Describe(stream));
            try
            {
                var caps = await DahuaApi.GetEncodeCapsAsync(api, BaseUrl, ct);
                if (caps.Count > 0)
                {
                    line("Capacidades:");
                    foreach (var cap in caps.Take(60)) line("    " + cap);
                }
            }
            catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException)
            {
                line($"Capacidades: no las informa ({QualityText.Explain(ex)})");
            }
        }
        catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException)
        {
            line($"No se pudo consultar: {QualityText.Explain(ex)}");
            line("(Muchas Imou traen esta API desactivada; basta con que responda ONVIF.)");
        }
    }

    /// <summary>Sends each change through ONVIF; one report line per change, accepted or not.</summary>
    public async Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<(string Token, EncoderChange Change)> changes)
    {
        var lines = new List<string>();
        using var http = CreateOnvifHttp();
        var client = new OnvifClient(http, DeviceUrl, camera.User, camera.Password);
        foreach (var (token, change) in changes)
        {
            try
            {
                await client.SetEncoderAsync(token, change);
                lines.Add($"{token}: enviado {QualityText.Values(change)} — aceptado.");
            }
            catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException or System.Xml.XmlException)
            {
                lines.Add($"{token}: la cámara no lo aceptó — {QualityText.Explain(ex)}");
            }
        }
        return lines;
    }
}
