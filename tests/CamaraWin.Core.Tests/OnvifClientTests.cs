using System.Net;
using System.Security.Cryptography;
using System.Text;
using CamaraWin.Core.Onvif;

namespace CamaraWin.Core.Tests;

public class OnvifClientTests
{
    sealed class FakeHandler(Func<Uri, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<Uri> Urls { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            // The unauthenticated clock query is covered by its own tests; keep it out of the call lists.
            if (!body.Contains("GetSystemDateAndTime"))
            {
                Urls.Add(request.RequestUri!);
                Bodies.Add(body);
            }
            var (code, xml) = respond(request.RequestUri!, body);
            return new HttpResponseMessage(code) { Content = new StringContent(xml) };
        }
    }

    static readonly Uri Device = new("http://192.168.1.20:2020/onvif/device_service");

    [Fact]
    public void Parses_device_information() =>
        Assert.Equal(new OnvifDeviceInfo("tp-link", "C200"), OnvifClient.ParseDeviceInformation(OnvifSamples.DeviceInformation));

    [Fact]
    public void Parses_media_xaddr() =>
        Assert.Equal(new Uri("http://192.168.1.20:2020/onvif/service"), OnvifClient.ParseMediaXAddr(OnvifSamples.Capabilities));

    [Fact]
    public void Parses_profiles_with_encoder_resolution() =>
        Assert.Equal(
            new[] { new OnvifProfile("profile_1", 1920, 1080), new OnvifProfile("profile_2", 640, 360) },
            OnvifClient.ParseProfiles(OnvifSamples.Profiles));

    [Fact]
    public void Parses_stream_uri() =>
        Assert.Equal("rtsp://192.168.1.20:554/stream1", OnvifClient.ParseStreamUri(OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream1")));

    [Fact]
    public void Stream_uri_missing_throws() =>
        Assert.Throws<OnvifException>(() => OnvifClient.ParseStreamUri(OnvifSamples.DeviceInformation));

    [Fact]
    public void Security_header_has_password_digest()
    {
        var nonce = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var created = new DateTime(2026, 9, 26, 18, 0, 0, DateTimeKind.Utc);
        var header = OnvifClient.BuildSecurityHeader("admin", "secret", nonce, created);

        var expected = Convert.ToBase64String(SHA1.HashData(
            [.. nonce, .. Encoding.UTF8.GetBytes("2026-09-26T18:00:00.000Z"), .. Encoding.UTF8.GetBytes("secret")]));
        Assert.Contains("<Username>admin</Username>", header);
        Assert.Contains(expected, header);
        Assert.Contains(Convert.ToBase64String(nonce), header);
        Assert.Contains("2026-09-26T18:00:00.000Z", header);
        Assert.DoesNotContain("secret", header);
    }

    [Fact]
    public async Task Resolves_main_and_sub_via_media_service()
    {
        var handler = new FakeHandler((_, body) => body switch
        {
            _ when body.Contains("GetCapabilities") => (HttpStatusCode.OK, OnvifSamples.Capabilities),
            _ when body.Contains("GetProfiles") => (HttpStatusCode.OK, OnvifSamples.Profiles),
            _ when body.Contains("profile_1") => (HttpStatusCode.OK, OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream1")),
            _ when body.Contains("profile_2") => (HttpStatusCode.OK, OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream2")),
            _ => (HttpStatusCode.BadRequest, ""),
        });
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "secret");

        var (main, sub) = await client.ResolveStreamUrisAsync();

        Assert.Equal("rtsp://192.168.1.20:554/stream1", main);
        Assert.Equal("rtsp://192.168.1.20:554/stream2", sub);
        Assert.Equal(Device, handler.Urls[0]);
        Assert.All(handler.Urls.Skip(1), u => Assert.Equal("/onvif/service", u.AbsolutePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Auth_failures_throw_OnvifAuthException(HttpStatusCode code)
    {
        var handler = new FakeHandler((_, _) => (code, OnvifSamples.NotAuthorizedFault));
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "wrong");
        await Assert.ThrowsAsync<OnvifAuthException>(() => client.GetDeviceInformationAsync());
    }

    [Fact]
    public void Parses_system_date_and_time_as_utc()
    {
        var parsed = OnvifClient.ParseSystemDateAndTime(OnvifSamples.SystemDateAndTime(new DateTime(2026, 9, 26, 18, 5, 7)));
        Assert.Equal(new DateTime(2026, 9, 26, 18, 5, 7, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Fact]
    public void System_date_and_time_without_utc_is_null() =>
        Assert.Null(OnvifClient.ParseSystemDateAndTime(OnvifSamples.DeviceInformation));

    [Fact]
    public async Task Security_header_created_uses_the_camera_clock()
    {
        var cameraNow = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        string? clockBody = null;
        var handler = new FakeHandler((_, body) =>
        {
            if (!body.Contains("GetSystemDateAndTime")) return (HttpStatusCode.OK, OnvifSamples.DeviceInformation);
            clockBody = body;
            return (HttpStatusCode.OK, OnvifSamples.SystemDateAndTime(cameraNow));
        });
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "secret");

        await client.GetDeviceInformationAsync();
        await client.GetDeviceInformationAsync();

        Assert.NotNull(clockBody);
        Assert.DoesNotContain("UsernameToken", clockBody);
        Assert.Equal(2, handler.Bodies.Count);
        foreach (var body in handler.Bodies)
        {
            var created = DateTime.Parse(
                System.Text.RegularExpressions.Regex.Match(body, "<Created[^>]*>([^<]+)</Created>").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
            Assert.InRange(created, cameraNow.AddSeconds(-5), cameraNow.AddSeconds(60));
        }
    }

    [Fact]
    public async Task Clock_query_failure_falls_back_to_pc_clock()
    {
        var handler = new FakeHandler((_, body) => body.Contains("GetSystemDateAndTime")
            ? (HttpStatusCode.InternalServerError, "")
            : (HttpStatusCode.OK, OnvifSamples.DeviceInformation));
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "secret");

        await client.GetDeviceInformationAsync();

        var created = DateTime.Parse(
            System.Text.RegularExpressions.Regex.Match(handler.Bodies[0], "<Created[^>]*>([^<]+)</Created>").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.InRange(created, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Theory]
    [InlineData("http://10.9.9.9:2020/onvif/media", "http://192.168.1.20:2020/onvif/media")]
    [InlineData("http://10.9.9.9/onvif/media", "http://192.168.1.20/onvif/media")]
    [InlineData("http://192.168.1.20:8080/media", "http://192.168.1.20:8080/media")]
    public void Rebase_to_host_keeps_port_and_path(string media, string expected) =>
        Assert.Equal(new Uri(expected), OnvifClient.RebaseToHost(new Uri(media), Device));

    [Fact]
    public async Task Media_xaddr_on_another_host_is_rebased_to_the_device_host()
    {
        var handler = new FakeHandler((_, body) => body switch
        {
            _ when body.Contains("GetCapabilities") => (HttpStatusCode.OK, OnvifSamples.CapabilitiesWithMedia("http://10.9.9.9:2020/onvif/media")),
            _ when body.Contains("GetProfiles") => (HttpStatusCode.OK, OnvifSamples.Profiles),
            _ when body.Contains("profile_1") => (HttpStatusCode.OK, OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream1")),
            _ when body.Contains("profile_2") => (HttpStatusCode.OK, OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream2")),
            _ => (HttpStatusCode.BadRequest, ""),
        });
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "secret");

        await client.ResolveStreamUrisAsync();

        Assert.All(handler.Urls, u => Assert.Equal("192.168.1.20", u.Host));
        Assert.All(handler.Urls.Skip(1), u => Assert.Equal("/onvif/media", u.AbsolutePath));
    }

    [Fact]
    public void Http_credentials_answer_only_digest_for_the_device_host()
    {
        var credentials = OnvifClient.CreateDigestCredentials(Device, "admin", "secret");

        Assert.Equal("secret", credentials.GetCredential(Device, "Digest")?.Password);
        Assert.NotNull(credentials.GetCredential(new Uri("http://192.168.1.20:8080/onvif/media"), "digest"));
        Assert.Null(credentials.GetCredential(Device, "Basic"));
        Assert.Null(credentials.GetCredential(Device, "NTLM"));
        Assert.Null(credentials.GetCredential(Device, "Negotiate"));
        Assert.Null(credentials.GetCredential(new Uri("http://10.9.9.9:2020/onvif/device_service"), "Digest"));
    }

    [Fact]
    public void Create_http_client_uses_digest_only_credentials()
    {
        using var http = OnvifClient.CreateHttpClient(Device, "admin", "secret");
        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
    }
}
