using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class StreamUrlBuilderTests
{
    static Camera Cam(Brand brand) => new()
    {
        Name = "cam", Brand = brand, Host = "192.168.1.20", User = "user", Password = "pass",
    };

    [Theory]
    [InlineData(StreamKind.Main, "rtsp://user:pass@192.168.1.20:554/stream1")]
    [InlineData(StreamKind.Sub, "rtsp://user:pass@192.168.1.20:554/stream2")]
    public void Tapo_urls(StreamKind kind, string expected) =>
        Assert.Equal(expected, StreamUrlBuilder.Build(Cam(Brand.Tapo), kind));

    [Theory]
    [InlineData(StreamKind.Main, "rtsp://user:pass@192.168.1.20:554/cam/realmonitor?channel=1&subtype=0")]
    [InlineData(StreamKind.Sub, "rtsp://user:pass@192.168.1.20:554/cam/realmonitor?channel=1&subtype=1")]
    public void Imou_urls(StreamKind kind, string expected) =>
        Assert.Equal(expected, StreamUrlBuilder.Build(Cam(Brand.Imou), kind));

    [Fact]
    public void Custom_port_is_used()
    {
        var cam = Cam(Brand.Tapo);
        cam.Port = 8554;
        Assert.Equal("rtsp://user:pass@192.168.1.20:8554/stream1", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Special_characters_in_credentials_are_escaped()
    {
        var cam = Cam(Brand.Tapo);
        cam.User = "ad min";
        cam.Password = "p@ss:w/rd";
        Assert.Equal("rtsp://ad%20min:p%40ss%3Aw%2Frd@192.168.1.20:554/stream1",
            StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Empty_user_omits_credentials()
    {
        var cam = Cam(Brand.Tapo);
        cam.User = "";
        Assert.Equal("rtsp://192.168.1.20:554/stream1", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Override_wins_and_gets_credentials()
    {
        var cam = Cam(Brand.Tapo);
        cam.MainUrlOverride = "rtsp://192.168.1.20:554/onvif/profile1";
        Assert.Equal("rtsp://user:pass@192.168.1.20:554/onvif/profile1", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Sub_falls_back_to_main_override()
    {
        var cam = Cam(Brand.Custom);
        cam.MainUrlOverride = "rtsp://host/x";
        Assert.Equal("rtsp://user:pass@host/x", StreamUrlBuilder.Build(cam, StreamKind.Sub));
    }

    [Fact]
    public void Blank_sub_override_falls_back_to_main()
    {
        var cam = Cam(Brand.Custom);
        cam.MainUrlOverride = "rtsp://host/x";
        cam.SubUrlOverride = "  ";
        Assert.Equal("rtsp://user:pass@host/x", StreamUrlBuilder.Build(cam, StreamKind.Sub));
    }

    [Fact]
    public void Override_with_existing_credentials_is_untouched()
    {
        var cam = Cam(Brand.Custom);
        cam.MainUrlOverride = "rtsp://a:b@host/x";
        Assert.Equal("rtsp://a:b@host/x", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Custom_without_override_throws()
    {
        Assert.Throws<InvalidOperationException>(() => StreamUrlBuilder.Build(Cam(Brand.Custom), StreamKind.Main));
    }

    [Fact]
    public void Clone_copies_values_and_keeps_id()
    {
        var cam = Cam(Brand.Imou);
        var copy = cam.Clone();
        Assert.NotSame(cam, copy);
        Assert.Equal(cam.Id, copy.Id);
        Assert.Equal(cam.Password, copy.Password);
    }
}
