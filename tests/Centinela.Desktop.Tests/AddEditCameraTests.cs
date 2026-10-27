using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

/// <summary>«Añadir cámara» and «Editar cámara» saved from the dialog: stored, saved to disk and the view rebuilt.</summary>
public sealed class AddEditCameraTests : IDisposable
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

    MainViewModel NewViewModel(CameraRepository cameras) =>
        new(cameras, new SettingsRepository(new SettingsStore(Path.Combine(_dir, "settings.json"))));

    [Fact]
    public void Added_camera_goes_last_is_saved_and_rebuilds()
    {
        var cameras = NewCameras();
        var vm = NewViewModel(cameras);
        var rebuilds = 0;
        vm.ViewChanged += () => rebuilds++;
        Assert.True(vm.IsEmpty);

        vm.AddCamera(new Camera { Name = "A", Host = "a" });
        vm.AddCamera(new Camera { Name = "A (copia)", Host = "a" });

        Assert.Equal([0, 1], cameras.All.Select(c => c.Order));
        Assert.Equal(["A", "A (copia)"], NewCameras().All.Select(c => c.Name));
        Assert.Equal(2, rebuilds);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Edited_camera_replaces_the_stored_one_and_is_saved()
    {
        var cameras = NewCameras();
        Camera a = new() { Name = "A", Host = "a" }, b = new() { Name = "B", Host = "b" };
        cameras.Add(a);
        cameras.Add(b);
        cameras.Save();
        var vm = NewViewModel(cameras);
        var rebuilds = 0;
        vm.ViewChanged += () => rebuilds++;

        var form = new CameraForm(a);
        Assert.True(form.TryApply(CameraForm.FieldsOf(a) with { Name = "Patio", Host = "10.0.0.9" }, out _, out _, out _));
        vm.ReplaceCamera(form.Result);

        Assert.Equal("Patio", cameras.Find(a.Id)!.Name);
        Assert.Equal("A", a.Name);
        var saved = NewCameras().All;
        Assert.Equal(["Patio", "B"], saved.Select(c => c.Name));
        Assert.Equal("10.0.0.9", saved[0].Host);
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public void Options_changed_elsewhere_while_editing_are_kept()
    {
        var cameras = NewCameras();
        var a = new Camera { Name = "A", Host = "a" };
        cameras.Add(a);
        cameras.Save();
        var vm = NewViewModel(cameras);
        var form = new CameraForm(a);

        // Toggled from another window while the dialog was open.
        a.Smoothing = true;
        a.MotionEnabled = true;
        a.LowQualityWhenBig = true;
        Assert.True(form.TryApply(CameraForm.FieldsOf(a) with { Name = "Patio" }, out _, out _, out _));
        vm.ReplaceCamera(form.Result);

        var saved = NewCameras().All.Single();
        Assert.Equal("Patio", saved.Name);
        Assert.True(saved.Smoothing);
        Assert.True(saved.MotionEnabled);
        Assert.True(saved.LowQualityWhenBig);
    }

    [Fact]
    public void Edit_of_a_camera_deleted_meanwhile_does_nothing()
    {
        var cameras = NewCameras();
        var vm = NewViewModel(cameras);
        var rebuilds = 0;
        vm.ViewChanged += () => rebuilds++;
        vm.ReplaceCamera(new Camera { Name = "Gone", Host = "g" });
        Assert.Empty(cameras.All);
        Assert.Equal(0, rebuilds);
    }
}
