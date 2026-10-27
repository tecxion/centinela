using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Centinela.Core.Onvif;

public sealed record OnvifDeviceInfo(string Manufacturer, string Model);

public sealed record OnvifProfile(string Token, int Width, int Height);

public class OnvifException(string message) : Exception(message);

public sealed class OnvifAuthException(string message) : OnvifException(message);

public sealed class OnvifClient(HttpClient http, Uri deviceServiceUrl, string user, string password)
{
    const string DeviceNs = "http://www.onvif.org/ver10/device/wsdl";
    const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";
    const string SchemaNs = "http://www.onvif.org/ver10/schema";

    readonly object _clockLock = new();
    Task<TimeSpan>? _clockOffset;

    /// <summary>
    /// HttpClient that also answers HTTP Digest challenges (some Dahua/Imou firmwares). It never answers
    /// Basic (or any other scheme), so the password is never sent in cleartext over plain HTTP.
    /// </summary>
    /// <param name="timeout">Per request, including the Digest retry; 5 s by default.</param>
    public static HttpClient CreateHttpClient(Uri deviceServiceUrl, string user, string password, TimeSpan? timeout = null) =>
        new(new HttpClientHandler { Credentials = CreateDigestCredentials(deviceServiceUrl, user, password), PreAuthenticate = false })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

    /// <summary>Credentials offered only for HTTP Digest challenges from the device's host (any port).</summary>
    public static ICredentials CreateDigestCredentials(Uri deviceServiceUrl, string user, string password) =>
        new DigestOnlyCredentials(deviceServiceUrl.Host, new NetworkCredential(user, password));

    sealed class DigestOnlyCredentials(string host, NetworkCredential credential) : ICredentials
    {
        public NetworkCredential? GetCredential(Uri uri, string authType) =>
            string.Equals(authType, "Digest", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
                ? credential
                : null;
    }

    /// <summary>Keeps the service's port and path but on the device's host (a camera may advertise a wrong IP).</summary>
    public static Uri RebaseToHost(Uri service, Uri device) => new UriBuilder(service) { Host = device.Host }.Uri;

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

    /// <summary>
    /// Read-only: every video encoder configuration with what it accepts (options null when the camera does not
    /// answer that query).
    /// </summary>
    public async Task<IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)>> GetEncoderSettingsAsync(
        CancellationToken ct = default)
    {
        var media = await GetMediaServiceUrlAsync(ct);
        var configurations = EncoderSettingsParser.ParseOnvifConfigurations(
            await SendAsync(media, $"<GetVideoEncoderConfigurations xmlns=\"{MediaNs}\"/>", ct));
        var result = new List<(string, EncoderSettings, EncoderOptions?)>();
        foreach (var (token, settings) in configurations)
        {
            EncoderOptions? options = null;
            try
            {
                options = EncoderSettingsParser.ParseOnvifOptions(await SendAsync(media,
                    $"<GetVideoEncoderConfigurationOptions xmlns=\"{MediaNs}\"><ConfigurationToken>{SecurityElement.Escape(token)}</ConfigurationToken></GetVideoEncoderConfigurationOptions>",
                    ct));
            }
            catch (OnvifException ex) when (ex is not OnvifAuthException)
            {
                // Options are a nice-to-have: the settings alone still answer the question.
            }
            result.Add((token, settings, options));
        }
        return result;
    }

    /// <summary>
    /// Writes a new resolution, frame rate and bitrate to one encoder configuration (persistent on the camera),
    /// starting from its current full configuration so nothing else changes. Throws <see cref="OnvifException"/>
    /// with the camera's reason when it refuses.
    /// </summary>
    public async Task SetEncoderAsync(string token, EncoderChange change, CancellationToken ct = default)
    {
        var media = await GetMediaServiceUrlAsync(ct);
        var current = await SendAsync(media, $"<GetVideoEncoderConfigurations xmlns=\"{MediaNs}\"/>", ct);
        await SendAsync(media, EncoderChangeBuilder.BuildSetConfiguration(current, token, change), ct);
    }

    async Task<Uri> GetMediaServiceUrlAsync(CancellationToken ct)
    {
        try
        {
            var xml = await SendAsync(deviceServiceUrl,
                $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>Media</Category></GetCapabilities>", ct);
            return ParseMediaXAddr(xml) is { } media ? RebaseToHost(media, deviceServiceUrl) : deviceServiceUrl;
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
        var offset = await GetClockOffsetAsync(ct);
        var header = BuildSecurityHeader(user, password, RandomNumberGenerator.GetBytes(16), DateTime.UtcNow + offset);
        return await PostAsync(url, $"<s:Header>{header}</s:Header>", body, ct);
    }

    /// <summary>
    /// Camera clock minus PC clock (UTC), queried once per client without authentication: cameras reject a
    /// UsernameToken whose Created is too far from their own clock. Zero when the query fails.
    /// </summary>
    Task<TimeSpan> GetClockOffsetAsync(CancellationToken ct)
    {
        lock (_clockLock)
        {
            if (_clockOffset is { IsCompleted: true, IsCompletedSuccessfully: false }) _clockOffset = null; // cancelled
            return _clockOffset ??= QueryClockOffsetAsync(ct);
        }
    }

    async Task<TimeSpan> QueryClockOffsetAsync(CancellationToken ct)
    {
        try
        {
            var xml = await PostAsync(deviceServiceUrl, "", $"<GetSystemDateAndTime xmlns=\"{DeviceNs}\"/>", ct);
            return ParseSystemDateAndTime(xml) is { } cameraUtc ? cameraUtc - DateTime.UtcNow : TimeSpan.Zero;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return TimeSpan.Zero;
        }
    }

    async Task<string> PostAsync(Uri url, string header, string body, CancellationToken ct)
    {
        var envelope = $"""<?xml version="1.0" encoding="utf-8"?><s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope">{header}<s:Body>{body}</s:Body></s:Envelope>""";
        using var content = new StringContent(envelope, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/soap+xml; charset=utf-8");

        using var response = await http.PostAsync(url, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || text.Contains("NotAuthorized", StringComparison.Ordinal))
            throw new OnvifAuthException("Usuario o contraseña ONVIF incorrectos.");
        if (!response.IsSuccessStatusCode)
            throw new OnvifException(EncoderChangeBuilder.FaultReason(text) is { Length: > 0 } reason
                ? $"ONVIF respondió {(int)response.StatusCode}: {reason}"
                : $"ONVIF respondió {(int)response.StatusCode}.");
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

    /// <summary>The camera's UTC clock from a GetSystemDateAndTimeResponse, or null when absent or invalid.</summary>
    public static DateTime? ParseSystemDateAndTime(string xml)
    {
        var utc = Find(XDocument.Parse(xml), "UTCDateTime");
        var date = utc is null ? null : Find(utc, "Date");
        var time = utc is null ? null : Find(utc, "Time");
        if (date is null || time is null) return null;
        try
        {
            return new DateTime(Int(date, "Year"), Int(date, "Month"), Int(date, "Day"),
                Int(time, "Hour"), Int(time, "Minute"), Int(time, "Second"), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
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
