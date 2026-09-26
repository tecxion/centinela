using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-tests-" + Guid.NewGuid());
    string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(GridMode.Auto, s.GridMode);
        Assert.Equal(1280, s.Width);
        Assert.Null(s.Left);
    }

    [Fact]
    public void Round_trip()
    {
        new SettingsStore(FilePath).Save(new AppSettings
        {
            Left = 10, Top = 20, Width = 900, Height = 600, Maximized = true, GridMode = GridMode.Nine,
        });
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal((10d, 20d, 900d, 600d, true, GridMode.Nine),
            (s.Left!.Value, s.Top!.Value, s.Width, s.Height, s.Maximized, s.GridMode));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Equal(GridMode.Auto, new SettingsStore(FilePath).Load().GridMode);
    }

    [Fact]
    public void Undefined_grid_mode_becomes_auto()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "width": 900, "gridMode": 12 }""");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(GridMode.Auto, s.GridMode);
        Assert.Equal(900, s.Width);
    }

    [Fact]
    public void Locked_file_gives_defaults()
    {
        new SettingsStore(FilePath).Save(new AppSettings { Width = 900, GridMode = GridMode.Nine });
        using var _ = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal((1280d, GridMode.Auto), (s.Width, s.GridMode));
    }

    [Fact]
    public void New_settings_round_trip()
    {
        var id = Guid.NewGuid();
        new SettingsStore(FilePath).Save(new AppSettings
        {
            LayoutMode = LayoutMode.Featured, FeaturedCameraId = id, ShowStats = true,
            BackupFolder = @"D:\Copias", TrayHintShown = true,
        });
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal((LayoutMode.Featured, id, true, @"D:\Copias", true),
            (s.LayoutMode, s.FeaturedCameraId!.Value, s.ShowStats, s.BackupFolder, s.TrayHintShown));
    }

    [Fact]
    public void Undefined_layout_mode_becomes_grid()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "layoutMode": 7 }""");
        Assert.Equal(LayoutMode.Grid, new SettingsStore(FilePath).Load().LayoutMode);
    }
}
