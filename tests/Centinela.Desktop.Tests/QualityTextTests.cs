using System.Net.Http;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Quality;

namespace Centinela.Desktop.Tests;

public class QualityTextTests
{
    [Fact]
    public void Describe_lists_every_reported_field()
    {
        var settings = new EncoderSettings("Main", "H264", 1920, 1080, 25, 2048, "CBR", 50);
        Assert.Equal("Main · H264 · 1920×1080 · 25 fps · 2048 kbps CBR · GOP 50", QualityText.Describe(settings));
    }

    [Fact]
    public void Describe_without_a_name_starts_with_a_placeholder()
    {
        var settings = new EncoderSettings("", "H265", 640, 360, 10, 256, null, 20);
        Assert.StartsWith("(sin nombre)", QualityText.Describe(settings));
    }

    [Fact]
    public void Describe_leaves_out_fields_reported_as_zero()
    {
        var settings = new EncoderSettings("Sub", "", 0, 0, 0, 0, "VBR", 0);
        Assert.Equal("Sub", QualityText.Describe(settings));
    }

    [Fact]
    public void Range_is_a_question_mark_when_not_reported()
    {
        Assert.Equal("?", QualityText.Range(0, 0));
        Assert.Equal("1–25", QualityText.Range(1, 25));
    }

    [Fact]
    public void Explain_a_timeout()
    {
        Assert.Equal("no contesta (tiempo agotado)", QualityText.Explain(new TaskCanceledException()));
    }

    [Fact]
    public void Explain_removes_credentials_from_urls()
    {
        var text = QualityText.Explain(new HttpRequestException("fallo en rtsp://u:p@h/stream"));
        Assert.DoesNotContain("p@", text);
        Assert.Contains("rtsp://h/stream", text);
    }

    [Fact]
    public void HostOf_takes_the_main_url_host_when_the_host_is_empty()
    {
        var camera = new Camera { Host = "  ", MainUrlOverride = "rtsp://admin:x@192.168.1.40:554/live" };
        Assert.Equal("192.168.1.40", QualityText.HostOf(camera));
        Assert.Equal("10.0.0.2", QualityText.HostOf(new Camera { Host = " 10.0.0.2 " }));
    }

    [Fact]
    public void Values_formats_a_change()
    {
        Assert.Equal("1280×720 · 10 fps · 768 kbps", QualityText.Values(new EncoderChange(1280, 720, 10, 768)));
    }
}
