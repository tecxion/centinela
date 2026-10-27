using Centinela.Core;
using Centinela.Desktop.Services.Streams;

namespace Centinela.Desktop.Tests;

/// <summary>Which shared session a camera still uses (pure: no session is created).</summary>
public class StreamLeasesTests
{
    static Camera Tapo() => new() { Name = "Patio", Brand = Brand.Tapo, Host = "192.168.1.20", Port = 554, User = "u", Password = "p" };

    [Fact]
    public void A_custom_camera_without_url_has_no_substream() =>
        Assert.Null(StreamLeases.SubUrlOf(new Camera { Brand = Brand.Custom }));

    [Fact]
    public void The_same_camera_still_uses_its_session()
    {
        var camera = Tapo();
        Assert.True(StreamLeases.UsesSession(camera, StreamLeases.SubUrlOf(camera)!, camera.UseUdp));
    }

    [Fact]
    public void An_edited_url_or_transport_is_another_session()
    {
        var camera = Tapo();
        var url = StreamLeases.SubUrlOf(camera)!;
        var useUdp = camera.UseUdp;
        camera.Password = "nueva";
        Assert.False(StreamLeases.UsesSession(camera, url, useUdp));
        var other = Tapo();
        other.UseUdp = !useUdp;
        Assert.False(StreamLeases.UsesSession(other, url, useUdp));
        Assert.False(StreamLeases.UsesSession(new Camera { Brand = Brand.Custom }, url, useUdp));
    }
}
