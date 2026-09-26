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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Urls.Add(request.RequestUri!);
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
}
