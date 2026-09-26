using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class BackupMergeTests
{
    [Fact]
    public void Matches_by_host_and_port_updates_and_appends()
    {
        var existing = new List<Camera>
        {
            new() { Name = "Viejo", Host = "192.168.1.20", Port = 554, Password = "keep", Order = 0 },
            new() { Name = "Otro", Host = "192.168.1.40", Port = 554, Password = "p", Order = 1 },
        };
        var imported = new List<Camera>
        {
            new() { Name = "Garaje", Host = "192.168.1.20", Port = 554, Password = "", User = "u" },
            new() { Name = "Nuevo", Host = "192.168.1.50", Port = 554, Password = "" },
        };
        var r = BackupMerge.Merge(existing, imported);
        Assert.Equal((1, 1, 1), (r.Added, r.Updated, r.WithoutPassword));
        var updated = r.Cameras.Single(c => c.Id == existing[0].Id);
        Assert.Equal(("Garaje", "u", "keep", 0), (updated.Name, updated.User, updated.Password, updated.Order));
        Assert.Contains(existing[0].Id, r.UpdatedIds);
        Assert.Equal(2, r.Cameras.Single(c => c.Name == "Nuevo").Order);
        Assert.Equal("p", existing[1].Password); // originals untouched
        Assert.Equal("Viejo", existing[0].Name);
    }

    [Fact]
    public void Imported_password_replaces_existing()
    {
        var existing = new List<Camera> { new() { Host = "h", Port = 554, Password = "old" } };
        var r = BackupMerge.Merge(existing, [new Camera { Host = "H", Port = 554, Password = "new" }]);
        Assert.Equal("new", r.Cameras[0].Password);
    }

    [Fact]
    public void Custom_cameras_match_by_main_url()
    {
        var existing = new List<Camera> { new() { Brand = Brand.Custom, MainUrlOverride = "rtsp://h/x" } };
        var r = BackupMerge.Merge(existing, [new Camera { Brand = Brand.Custom, MainUrlOverride = "RTSP://h/x", Name = "N" }]);
        Assert.Equal((0, 1), (r.Added, r.Updated));
    }
}
