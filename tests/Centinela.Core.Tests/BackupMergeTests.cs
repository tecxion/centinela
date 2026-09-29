using Centinela.Core;

namespace Centinela.Core.Tests;

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

    [Fact]
    public void Passwordless_import_cannot_redirect_stored_password_to_other_urls()
    {
        var existing = new List<Camera> { new() { Host = "h", Port = 554, Password = "keep", MainUrlOverride = null, SubUrlOverride = "rtsp://h/sub" } };
        var r = BackupMerge.Merge(existing, [new Camera { Host = "h", Port = 554, Password = "", MainUrlOverride = "rtsp://evil/x", SubUrlOverride = "rtsp://evil/y" }]);
        Assert.Equal(("keep", (string?)null, "rtsp://h/sub"), (r.Cameras[0].Password, r.Cameras[0].MainUrlOverride, r.Cameras[0].SubUrlOverride));
    }

    [Fact]
    public void Import_with_password_may_change_urls()
    {
        var existing = new List<Camera> { new() { Host = "h", Port = 554, Password = "keep" } };
        var r = BackupMerge.Merge(existing, [new Camera { Host = "h", Port = 554, Password = "new", MainUrlOverride = "rtsp://h/x" }]);
        Assert.Equal(("new", "rtsp://h/x"), (r.Cameras[0].Password, r.Cameras[0].MainUrlOverride));
    }

    [Fact]
    public void Duplicate_imported_entries_are_counted_once()
    {
        var r = BackupMerge.Merge([],
        [
            new Camera { Name = "A", Host = "h", Port = 554, Password = "" },
            new Camera { Name = "B", Host = "H", Port = 554, Password = "" },
        ]);
        Assert.Equal((1, 0, 1), (r.Added, r.Updated, r.WithoutPassword));
        Assert.Empty(r.UpdatedIds);
        Assert.Equal("B", Assert.Single(r.Cameras).Name);
    }

    [Fact]
    public void Reimporting_identical_export_updates_nothing()
    {
        var existing = new List<Camera>
        {
            new() { Name = "Garaje", Brand = Brand.Tapo, Host = "192.168.1.20", Port = 554, User = "u", Password = "secret", Order = 0 },
            new() { Name = "Rtsp", Brand = Brand.Custom, MainUrlOverride = "rtsp://h/x", SubUrlOverride = "rtsp://h/y", UseUdp = true, Order = 1 },
        };
        // A password-less export: same fields, no passwords, overrides differing only in case.
        var imported = existing.Select(c => { var copy = c.Clone(); copy.Password = ""; return copy; }).ToList();
        imported[1].MainUrlOverride = "RTSP://h/x";
        var r = BackupMerge.Merge(existing, imported);
        Assert.Equal((0, 0), (r.Added, r.Updated));
        Assert.Empty(r.UpdatedIds);
    }

    [Fact]
    public void Same_password_is_not_a_change_but_a_new_one_is()
    {
        var existing = new List<Camera> { new() { Host = "h", Port = 554, Password = "p" } };
        Assert.Equal(0, BackupMerge.Merge(existing, [new Camera { Host = "h", Port = 554, Password = "p" }]).Updated);
        Assert.Equal(1, BackupMerge.Merge(existing, [new Camera { Host = "h", Port = 554, Password = "q" }]).Updated);
        Assert.Equal(1, BackupMerge.Merge(existing, [new Camera { Host = "h", Port = 554, Password = "p", UseUdp = true }]).Updated);
    }

    [Fact]
    public void Existing_camera_matched_twice_counts_once()
    {
        var existing = new List<Camera> { new() { Host = "h", Port = 554, Password = "" } };
        var r = BackupMerge.Merge(existing, [new Camera { Host = "h", Port = 554, Name = "A" }, new Camera { Host = "h", Port = 554, Name = "B" }]);
        Assert.Equal((0, 1, 1), (r.Added, r.Updated, r.WithoutPassword));
    }

    [Fact]
    public void Merge_copies_motion_fields_and_counts_them_as_changes()
    {
        var existing = new Camera { Name = "A", Host = "h", Port = 554 };
        var incoming = existing.Clone();
        incoming.MotionEnabled = true;
        incoming.MotionAlerts = false;
        var result = BackupMerge.Merge([existing], [incoming]);
        Assert.Equal(1, result.Updated);
        Assert.True(result.Cameras[0].MotionEnabled);
        Assert.False(result.Cameras[0].MotionAlerts);
    }

    [Fact]
    public void Merge_copies_low_quality_when_big_and_counts_it_as_a_change()
    {
        var existing = new Camera { Name = "A", Host = "h", Port = 554 };
        var incoming = existing.Clone();
        incoming.LowQualityWhenBig = true;
        var result = BackupMerge.Merge([existing], [incoming]);
        Assert.Equal(1, result.Updated);
        Assert.True(result.Cameras[0].LowQualityWhenBig);
    }

    [Fact]
    public void Merge_copies_smoothing_and_counts_it_as_a_change()
    {
        var existing = new Camera { Name = "A", Host = "h", Port = 554 };
        var incoming = existing.Clone();
        incoming.Smoothing = true;
        var result = BackupMerge.Merge([existing], [incoming]);
        Assert.Equal(1, result.Updated);
        Assert.True(result.Cameras[0].Smoothing);
    }
}
