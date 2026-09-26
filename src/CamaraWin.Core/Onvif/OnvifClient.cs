using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CamaraWin.Core.Onvif;

public sealed record OnvifDeviceInfo(string Manufacturer, string Model);

public sealed record OnvifProfile(string Token, int Width, int Height);

public class OnvifException(string message) : Exception(message);

public sealed class OnvifAuthException(string message) : OnvifException(message);

public sealed class OnvifClient(HttpClient http, Uri deviceServiceUrl, string user, string password)
{
    const string DeviceNs = "http://www.onvif.org/ver10/device/wsdl";
    const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";
    const string SchemaNs = "http://www.onvif.org/ver10/schema";

    /// <summary>HttpClient that also answers HTTP Digest challenges (some Dahua/Imou firmwares).</summary>
    public static HttpClient CreateHttpClient(string user, string password) =>
        new(new HttpClientHandler { Credentials = new NetworkCredential(user, password) })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

    public async Task<OnvifDeviceInfo> GetDeviceInformationAsync(CancellationToken ct = default) =>
        ParseDeviceInformation(await SendAsync(deviceServiceUrl, $"<GetDeviceInformation xmlns=\"{DeviceNs}\"/>", ct));

    public async Task<(string Main, string Sub)> ResolveStreamUrisAsync(CancellationToken ct = default)
    {
        var media = await GetMediaServiceUrlAsync(ct);
        var profiles = ParseProfiles(await SendAsync(media, $"<GetProfiles xmlns=\"{MediaNs}\"/>", ct));
        if (profiles.Count == 0) throw new OnvifException("La cámara no tiene perfiles de vídeo.");

        var ordered = profiles.OrderByDescending(p => p.Width * p.Height).ToList();
        var main = await GetStreamUriAsync(media, ordered[0].Token, ct);
        var sub = ordered.Count > 1 ? await GetStreamUriAsync(media, ordered[^1].Token, ct) : main;
        return (main, sub);
    }

    async Task<Uri> GetMediaServiceUrlAsync(CancellationToken ct)
    {
        try
        {
            var xml = await SendAsync(deviceServiceUrl,
                $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>Media</Category></GetCapabilities>", ct);
            return ParseMediaXAddr(xml) ?? deviceServiceUrl;
        }
        catch (OnvifException ex) when (ex is not OnvifAuthException)
        {
            return deviceServiceUrl;
        }
    }

    async Task<string> GetStreamUriAsync(Uri media, string token, CancellationToken ct) =>
        ParseStreamUri(await SendAsync(media, $"""
            <GetStreamUri xmlns="{MediaNs}"><StreamSetup><Stream xmlns="{SchemaNs}">RTP-Unicast</Stream><Transport xmlns="{SchemaNs}"><Protocol>RTSP</Protocol></Transport></StreamSetup><ProfileToken>{SecurityElement.Escape(token)}</ProfileToken></GetStreamUri>
            """, ct));

    async Task<string> SendAsync(Uri url, string body, CancellationToken ct)
    {
        var header = BuildSecurityHeader(user, password, RandomNumberGenerator.GetBytes(16), DateTime.UtcNow);
        var envelope = $"""<?xml version="1.0" encoding="utf-8"?><s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Header>{header}</s:Header><s:Body>{body}</s:Body></s:Envelope>""";
        using var content = new StringContent(envelope, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/soap+xml; charset=utf-8");

        using var response = await http.PostAsync(url, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || text.Contains("NotAuthorized", StringComparison.Ordinal))
            throw new OnvifAuthException("Usuario o contraseña ONVIF incorrectos.");
        if (!response.IsSuccessStatusCode)
            throw new OnvifException($"ONVIF respondió {(int)response.StatusCode}.");
        return text;
    }

    public static string BuildSecurityHeader(string user, string password, byte[] nonce, DateTime createdUtc)
    {
        var created = createdUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var digest = Convert.ToBase64String(SHA1.HashData(
            [.. nonce, .. Encoding.UTF8.GetBytes(created), .. Encoding.UTF8.GetBytes(password)]));
        return $"""<Security s:mustUnderstand="1" xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"><UsernameToken><Username>{SecurityElement.Escape(user)}</Username><Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</Password><Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{Convert.ToBase64String(nonce)}</Nonce><Created xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">{created}</Created></UsernameToken></Security>""";
    }

    public static OnvifDeviceInfo ParseDeviceInformation(string xml)
    {
        var doc = XDocument.Parse(xml);
        return new OnvifDeviceInfo(Find(doc, "Manufacturer")?.Value.Trim() ?? "", Find(doc, "Model")?.Value.Trim() ?? "");
    }

    public static Uri? ParseMediaXAddr(string xml)
    {
        var media = Find(XDocument.Parse(xml), "Media");
        var address = media is null ? null : Find(media, "XAddr")?.Value.Trim();
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
    }

    public static IReadOnlyList<OnvifProfile> ParseProfiles(string xml) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "Profiles")
            .Select(p =>
            {
                var encoder = Find(p, "VideoEncoderConfiguration");
                var resolution = encoder is null ? null : Find(encoder, "Resolution");
                return new OnvifProfile((string?)p.Attribute("token") ?? "", Int(resolution, "Width"), Int(resolution, "Height"));
            })
            .Where(p => p.Token.Length > 0)
            .ToList();

    public static string ParseStreamUri(string xml)
    {
        var media = Find(XDocument.Parse(xml), "MediaUri");
        var uri = media is null ? null : Find(media, "Uri")?.Value.Trim();
        return string.IsNullOrEmpty(uri) ? throw new OnvifException("GetStreamUri no devolvió ninguna URL.") : uri;
    }

    static XElement? Find(XContainer container, string localName) =>
        container.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    static int Int(XElement? parent, string localName) =>
        int.TryParse(parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value, out var value) ? value : 0;
}
