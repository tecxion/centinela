using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public sealed class CameraStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-tests-" + Guid.NewGuid());
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
}
