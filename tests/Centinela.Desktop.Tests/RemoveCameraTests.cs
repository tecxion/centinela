using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

/// <summary>«Eliminar»: the camera leaves the list and the files, and the featured / «Dos principales» choice forgets it.</summary>
public sealed class RemoveCameraTests : IDisposable
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

    SettingsRepository NewSettings() => new(new SettingsStore(Path.Combine(_dir, "settings.json")));

    [Fact]
    public void Removes_saves_forgets_the_choice_and_rebuilds()
    {
        var cameras = NewCameras();
        Camera a = new() { Name = "A", Host = "a" }, b = new() { Name = "B", Host = "b" }, c = new() { Name = "C", Host = "c" };
        cameras.Add(a);
        cameras.Add(b);
        cameras.Add(c);
        cameras.Save();
        var settings = NewSettings();
        settings.Settings.FeaturedCameraId = b.Id;
        settings.Settings.DualCameraIds = [a.Id, b.Id];
        var vm = new MainViewModel(cameras, settings);
        var rebuilds = 0;
        vm.ViewChanged += () => rebuilds++;

        vm.RemoveCamera(b.Id);

        Assert.Equal(["A", "C"], cameras.All.Select(x => x.Name));
        Assert.Equal(["A", "C"], NewCameras().All.Select(x => x.Name));
        var saved = NewSettings().Settings;
        Assert.Null(saved.FeaturedCameraId);
        Assert.Equal([a.Id], saved.DualCameraIds);
        Assert.Equal(1, rebuilds);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Another_featured_camera_stays_and_the_last_one_leaves_it_empty()
    {
        var cameras = NewCameras();
        Camera a = new() { Name = "A", Host = "a" };
        cameras.Add(a);
        var settings = NewSettings();
        var other = Guid.NewGuid();
        settings.Settings.FeaturedCameraId = other;
        var vm = new MainViewModel(cameras, settings);

        vm.RemoveCamera(a.Id);

        Assert.Equal(other, settings.Settings.FeaturedCameraId);
        Assert.True(vm.IsEmpty);
    }
}
