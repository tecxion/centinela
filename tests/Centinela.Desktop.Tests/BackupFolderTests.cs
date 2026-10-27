using Centinela.Core;
using Centinela.Desktop.Services.Backup;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

/// <summary>«Carpeta de copia automática…»: the chosen folder, or Documents\Centinela\backup when none is set.</summary>
public sealed class BackupFolderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());
    readonly List<string> _scheduled = [];

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

    MainViewModel NewViewModel(SettingsRepository settings) =>
        new(NewCameras(), settings, new AutomaticBackup((folder, _) => { lock (_scheduled) _scheduled.Add(folder); }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_folder_set_it_is_the_default_one(string? folder)
    {
        var settings = NewSettings();
        settings.Settings.BackupFolder = folder;
        Assert.Equal(AppPaths.DefaultBackupDirectory, NewViewModel(settings).BackupFolder);
    }

    [Fact]
    public void The_folder_set_is_used()
    {
        var settings = NewSettings();
        settings.Settings.BackupFolder = @"D:\copias";
        Assert.Equal(@"D:\copias", NewViewModel(settings).BackupFolder);
    }

    [Fact]
    public async Task Setting_the_folder_saves_it_and_schedules_a_copy_there()
    {
        var folder = Path.Combine(_dir, "copias");
        var vm = NewViewModel(NewSettings());

        vm.SetBackupFolder(folder);
        await vm.Backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(folder, NewSettings().Settings.BackupFolder);
        Assert.Equal(folder, vm.BackupFolder);
        Assert.Equal([folder], _scheduled);
    }

    [Fact]
    public async Task Every_successful_save_of_the_cameras_schedules_a_copy()
    {
        var settings = NewSettings();
        settings.Settings.BackupFolder = Path.Combine(_dir, "copias");
        var vm = NewViewModel(settings);

        vm.AddCamera(new Camera { Name = "A", Host = "a" });
        await vm.Backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([Path.Combine(_dir, "copias")], _scheduled);
    }
}
