using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

/// <summary>Applying an imported list: merged, saved, updated cameras' tiles retired and the view rebuilt.</summary>
public sealed class ImportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    CameraRepository NewCameras()
    {
        var cameras = new CameraRepository(new CameraStore(Path.Combine(_dir, "cameras.json"), new FakeSecretStore()));
        cameras.Load();
        return cameras;
    }

    [Fact]
    public void Adds_new_updates_matching_retires_their_tiles_and_saves()
    {
        var cameras = NewCameras();
        Camera a = new() { Name = "A", Host = "10.0.0.1", Port = 554, Password = "pa" };
        Camera b = new() { Name = "B", Host = "10.0.0.2", Port = 554, Password = "pb" };
        cameras.Add(a);
        cameras.Add(b);
        cameras.Save();
        var vm = new MainViewModel(cameras, new SettingsRepository(new SettingsStore(Path.Combine(_dir, "settings.json"))));
        var rebuilds = 0;
        vm.ViewChanged += () => rebuilds++;
        var retired = new List<Guid>();
        var imported = new BackupImport(
        [
            new Camera { Name = "Nueva", Host = "10.0.0.3", Port = 554, Password = "pn", Order = 0 },
            new Camera { Name = "Patio", Host = "10.0.0.2", Port = 554, Password = "pb", Order = 1 },
        ], WasEncrypted: true);

        var summary = vm.ApplyImport(imported, retired.Add);

        Assert.Equal("1 añadidas, 1 actualizadas, 0 sin contraseña.", summary);
        Assert.Equal([b.Id], retired);
        var saved = NewCameras().All;
        Assert.Equal(3, saved.Count);
        Assert.Equal(["A", "Patio", "Nueva"], saved.Select(c => c.Name));
        Assert.Equal(["A", "Patio", "Nueva"], cameras.All.Select(c => c.Name));
        Assert.Equal(1, rebuilds);
    }
}
