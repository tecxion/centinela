using Centinela.Core;

namespace Centinela.Core.Tests;

public class CameraDuplicateTests
{
    [Fact]
    public void Duplicate_gets_new_id_and_copy_suffix_and_keeps_everything_else()
    {
        var original = new Camera
        {
            Name = "Garaje", Brand = Brand.Imou, Host = "192.168.1.20", Port = 554, User = "admin",
            Password = "secreto", MainUrlOverride = "rtsp://h/main", SubUrlOverride = "rtsp://h/sub", UseUdp = true, Order = 3,
        };
        var copy = original.Duplicate();
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Garaje (copia)", copy.Name);
        Assert.Equal(("192.168.1.20", 554, "admin", "secreto"), (copy.Host, copy.Port, copy.User, copy.Password));
        Assert.Equal((Brand.Imou, "rtsp://h/main", "rtsp://h/sub", true), (copy.Brand, copy.MainUrlOverride, copy.SubUrlOverride, copy.UseUdp));
        Assert.Equal("Garaje", original.Name);
    }
}
