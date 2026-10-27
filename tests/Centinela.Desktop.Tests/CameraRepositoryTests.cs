using Centinela.Core;
using Centinela.Desktop.Services.Data;

namespace Centinela.Desktop.Tests;

public sealed class CameraRepositoryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());
    string FilePath => Path.Combine(_dir, "cameras.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    CameraRepository NewRepository() => new(new CameraStore(FilePath, new FakeSecretStore()));

    [Fact]
    public void Load_orders_cameras_and_renumbers_them_from_zero()
    {
        new CameraStore(FilePath, new FakeSecretStore()).Save([
            new Camera { Name = "B", Host = "b", Order = 7 }, new Camera { Name = "A", Host = "a", Order = 3 }]);
        var repo = NewRepository();
        repo.Load();
        Assert.Equal(["A", "B"], repo.All.Select(c => c.Name));
        Assert.Equal([0, 1], repo.All.Select(c => c.Order));
    }

    [Fact]
    public void Add_goes_last_replace_and_remove_by_id()
    {
        var repo = NewRepository();
        repo.Load();
        var a = new Camera { Name = "A", Host = "a" };
        var b = new Camera { Name = "B", Host = "b" };
        repo.Add(a);
        repo.Add(b);
        Assert.Equal([0, 1], repo.All.Select(c => c.Order));
        var edited = b.Clone();
        edited.Name = "B2";
        repo.Replace(edited);
        Assert.Equal("B2", repo.Find(b.Id)!.Name);
        repo.Remove(a.Id);
        Assert.Equal(["B2"], repo.All.Select(c => c.Name));
    }

    [Fact]
    public void CurrentOr_prefers_the_stored_camera()
    {
        var repo = NewRepository();
        repo.Load();
        var a = new Camera { Name = "A", Host = "a" };
        repo.Add(a);
        var old = a.Clone();
        old.Name = "old copy";
        Assert.Same(a, repo.CurrentOr(old));
        var deleted = new Camera { Name = "gone" };
        Assert.Same(deleted, repo.CurrentOr(deleted));
    }

    [Fact]
    public void Save_writes_and_reports_failures_without_throwing()
    {
        var repo = NewRepository();
        repo.Load();
        repo.Add(new Camera { Name = "A", Host = "a" });
        var saved = 0;
        repo.Saved += () => saved++;
        Assert.True(repo.Save());
        Assert.Equal(1, saved);
        Assert.True(File.Exists(FilePath));

        // A directory where the file should be makes the save fail.
        File.Delete(FilePath);
        Directory.CreateDirectory(FilePath);
        string? failure = null;
        repo.SaveFailed += message => failure = message;
        Assert.False(repo.Save());
        Assert.StartsWith("No se pudieron guardar las cámaras: ", failure);
    }
}
