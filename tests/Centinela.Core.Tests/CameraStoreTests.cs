using Centinela.Core;

namespace Centinela.Core.Tests;

public sealed class CameraStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-tests-" + Guid.NewGuid());
    string FilePath => Path.Combine(_dir, "cameras.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static Camera Sample(int order) => new()
    {
        Name = $"Cam {order}", Brand = Brand.Imou, Host = "192.168.1.30", Port = 8554,
        User = "admin", Password = "SuperSecreta123", MainUrlOverride = "rtsp://h/main",
        SubUrlOverride = "rtsp://h/sub", UseUdp = true, Order = order,
    };

    [Fact]
    public void Load_returns_empty_when_file_missing() =>
        Assert.Empty(new CameraStore(FilePath).Load());

    [Fact]
    public void Round_trip_preserves_all_fields()
    {
        var original = Sample(3);
        new CameraStore(FilePath).Save([original]);

        var loaded = Assert.Single(new CameraStore(FilePath).Load());
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Brand, loaded.Brand);
        Assert.Equal(original.Host, loaded.Host);
        Assert.Equal(original.Port, loaded.Port);
        Assert.Equal(original.User, loaded.User);
        Assert.Equal(original.Password, loaded.Password);
        Assert.Equal(original.MainUrlOverride, loaded.MainUrlOverride);
        Assert.Equal(original.SubUrlOverride, loaded.SubUrlOverride);
        Assert.Equal(original.UseUdp, loaded.UseUdp);
        Assert.Equal(original.Order, loaded.Order);
    }

    [Fact]
    public void Password_is_not_stored_in_plaintext()
    {
        new CameraStore(FilePath).Save([Sample(0)]);
        var json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("SuperSecreta123", json);
        Assert.Contains("passwordProtected", json);
    }

    [Fact]
    public void Load_returns_cameras_sorted_by_order()
    {
        new CameraStore(FilePath).Save([Sample(2), Sample(0), Sample(1)]);
        Assert.Equal(new[] { 0, 1, 2 }, new CameraStore(FilePath).Load().Select(c => c.Order));
    }

    static string CameraJson(string passwordProtected) => $$"""
        [
          {
            "id": "11111111-2222-3333-4444-555555555555",
            "name": "Cam",
            "brand": "Imou",
            "host": "192.168.1.30",
            "port": 554,
            "user": "admin",
            "passwordProtected": "{{passwordProtected}}",
            "useUdp": false,
            "order": 0
          }
        ]
        """;

    [Fact]
    public void Undecryptable_password_loads_as_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, CameraJson("AAAA"));
        var loaded = Assert.Single(new CameraStore(FilePath).Load());
        Assert.Equal("Cam", loaded.Name);
        Assert.Equal("", loaded.Password);
    }

    [Fact]
    public void Invalid_base64_password_loads_as_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, CameraJson("not base64!"));
        var loaded = Assert.Single(new CameraStore(FilePath).Load());
        Assert.Equal("Cam", loaded.Name);
        Assert.Equal("", loaded.Password);
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_load_returns_empty()
    {
        Directory.CreateDirectory(_dir);
        const string content = "{ not json";
        File.WriteAllText(FilePath, content);

        Assert.Empty(new CameraStore(FilePath).Load());

        Assert.False(File.Exists(FilePath));
        var moved = Assert.Single(Directory.GetFiles(_dir, "cameras.json.bad-*"));
        Assert.Equal(content, File.ReadAllText(moved));
    }

    [Fact]
    public void Credentials_in_override_urls_are_moved_to_protected_storage()
    {
        var camera = new Camera
        {
            Name = "Otra", Brand = Brand.Custom, MainUrlOverride = "rtsp://bob:s3cr%40t@h/x",
            SubUrlOverride = "rtsp://bob:s3cr%40t@h/y",
        };
        new CameraStore(FilePath).Save([camera]);

        var json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("s3cr", json);
        Assert.DoesNotContain("bob:", json);

        var loaded = Assert.Single(new CameraStore(FilePath).Load());
        Assert.Equal("bob", loaded.User);
        Assert.Equal("s3cr@t", loaded.Password);
        Assert.Equal("rtsp://h/x", loaded.MainUrlOverride);
        Assert.Equal("rtsp://h/y", loaded.SubUrlOverride);
        Assert.Equal("rtsp://bob:s3cr%40t@h/x", StreamUrlBuilder.Build(loaded, StreamKind.Main));
    }

    [Fact]
    public void Override_credentials_are_stripped_when_user_fields_are_set()
    {
        var camera = Sample(0);
        camera.MainUrlOverride = "rtsp://other:pw@h:554/main";
        new CameraStore(FilePath).Save([camera]);

        Assert.DoesNotContain("other:pw", File.ReadAllText(FilePath));
        var loaded = Assert.Single(new CameraStore(FilePath).Load());
        Assert.Equal("admin", loaded.User);
        Assert.Equal("SuperSecreta123", loaded.Password);
        Assert.Equal("rtsp://h:554/main", loaded.MainUrlOverride);
    }

    [Fact]
    public void Locked_file_loads_empty_and_is_left_untouched()
    {
        new CameraStore(FilePath).Save([Sample(0)]);
        var before = File.ReadAllText(FilePath);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Empty(new CameraStore(FilePath).Load());

        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "cameras.json.bad-*"));
    }

    [Fact]
    public void Corrupt_file_that_cannot_be_moved_is_left_untouched()
    {
        Directory.CreateDirectory(_dir);
        const string content = "{ not json";
        File.WriteAllText(FilePath, content);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Empty(new CameraStore(FilePath).Load());

        Assert.Equal(content, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "cameras.json.bad-*"));
    }

    [Fact]
    public void Undefined_brand_loads_as_custom()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, CameraJson("").Replace("\"brand\": \"Imou\"", "\"brand\": 7"));
        Assert.Equal(Brand.Custom, Assert.Single(new CameraStore(FilePath).Load()).Brand);
    }

    [Fact]
    public void Motion_and_alert_fields_round_trip()
    {
        var path = Path.Combine(_dir, "c.json");
        var store = new CameraStore(path);
        store.Save([new Camera { Name = "A", Host = "h", MotionEnabled = true, MotionSensitivity = MotionSensitivity.High,
            MotionCooldownSeconds = 300, ConnectionAlerts = false, MotionAlerts = false }]);
        var c = Assert.Single(store.Load());
        Assert.Equal((true, MotionSensitivity.High, 300, false, false),
            (c.MotionEnabled, c.MotionSensitivity, c.MotionCooldownSeconds, c.ConnectionAlerts, c.MotionAlerts));
    }

    [Fact]
    public void Low_quality_when_big_round_trips_and_defaults_to_off()
    {
        var path = Path.Combine(_dir, "q.json");
        var store = new CameraStore(path);
        store.Save([new Camera { Name = "A", Host = "h", LowQualityWhenBig = true, Smoothing = true }, new Camera { Name = "B", Host = "h2" }]);
        var cams = store.Load();
        Assert.True(cams.Single(c => c.Name == "A").LowQualityWhenBig);
        Assert.True(cams.Single(c => c.Name == "A").Smoothing);
        Assert.False(cams.Single(c => c.Name == "B").LowQualityWhenBig);
        Assert.False(cams.Single(c => c.Name == "B").Smoothing);
    }

    [Fact]
    public void V12_file_without_new_fields_gets_defaults_and_bad_values_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, """
            [ { "id": "6f1c6a36-4a6e-4a53-9a0f-1d0e0b0a0c01", "name": "Vieja", "brand": "Tapo", "host": "h", "port": 554, "user": "u", "order": 0 },
              { "id": "6f1c6a36-4a6e-4a53-9a0f-1d0e0b0a0c02", "name": "Rara", "brand": "Tapo", "host": "h2", "port": 554, "user": "u", "order": 1,
                "motionCooldownSeconds": 45, "motionSensitivity": 7 } ]
            """);
        var cams = new CameraStore(path).Load();
        Assert.Equal((false, MotionSensitivity.Medium, 60, true, true),
            (cams[0].MotionEnabled, cams[0].MotionSensitivity, cams[0].MotionCooldownSeconds, cams[0].ConnectionAlerts, cams[0].MotionAlerts));
        Assert.Equal((MotionSensitivity.Medium, 60), (cams[1].MotionSensitivity, cams[1].MotionCooldownSeconds));
    }

    [Fact]
    public void Unknown_enum_names_fall_back_instead_of_quarantining_the_file()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "unknown.json");
        File.WriteAllText(path, """
            [ { "id": "6f1c6a36-4a6e-4a53-9a0f-1d0e0b0a0c03", "name": "Nueva", "brand": "Marciana", "host": "h", "port": 554,
                "user": "u", "order": 0, "motionEnabled": true, "motionSensitivity": "Extreme" } ]
            """);
        var c = Assert.Single(new CameraStore(path).Load());
        Assert.Equal((Brand.Custom, MotionSensitivity.Medium, true), (c.Brand, c.MotionSensitivity, c.MotionEnabled));
        Assert.Empty(Directory.GetFiles(_dir, "unknown.json.bad-*"));
    }
}
