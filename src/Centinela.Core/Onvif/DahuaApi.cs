using System.Net;

namespace Centinela.Core.Onvif;

/// <summary>
/// Read-only queries to the Dahua HTTP API that Imou cameras also run (HTTP Digest only, through
/// <see cref="OnvifClient.CreateHttpClient"/>, so the password never travels in cleartext).
/// </summary>
public static class DahuaApi
{
    /// <summary>The current encoder settings (main and sub streams of the first channel).</summary>
    public static async Task<IReadOnlyList<EncoderSettings>> GetEncodeAsync(HttpClient http, Uri baseUrl, CancellationToken ct = default) =>
        EncoderSettingsParser.ParseDahuaEncode(await GetAsync(http, new Uri(baseUrl, "/cgi-bin/configManager.cgi?action=getConfig&name=Encode"), ct));

    /// <summary>The encoder capabilities as the camera words them (lines about video only), for the report.</summary>
    public static async Task<IReadOnlyList<string>> GetEncodeCapsAsync(HttpClient http, Uri baseUrl, CancellationToken ct = default)
    {
        var text = await GetAsync(http, new Uri(baseUrl, "/cgi-bin/encode.cgi?action=getConfigCaps&channel=1"), ct);
        return text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains(".Video.", StringComparison.Ordinal) && line.Contains('='))
            .ToList();
    }

    static async Task<string> GetAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new OnvifAuthException("Usuario o contraseña incorrectos para la API de la cámara.");
        if (!response.IsSuccessStatusCode)
            throw new OnvifException($"La API de la cámara respondió {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(ct);
    }
}
