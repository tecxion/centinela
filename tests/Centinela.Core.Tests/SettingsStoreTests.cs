using Centinela.Core;

namespace Centinela.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-tests-" + Guid.NewGuid());
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
    public void Discovery_networks_round_trip_and_null_becomes_empty()
    {
        new SettingsStore(FilePath).Save(new AppSettings { DiscoveryNetworks = ["10.20.30.0/24", "192.168.2.1-50"] });
        Assert.Equal(["10.20.30.0/24", "192.168.2.1-50"], new SettingsStore(FilePath).Load().DiscoveryNetworks);

        File.WriteAllText(FilePath, """{ "discoveryNetworks": null }""");
        Assert.Empty(new SettingsStore(FilePath).Load().DiscoveryNetworks);
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

    [Fact]
    public void New_settings_have_v12_defaults()
    {
        var s = new SettingsStore(Path.Combine(_dir, "none.json")).Load();
        Assert.True(s.CheckUpdatesOnStartup);
        Assert.Null(s.LastUpdateCheck);
        Assert.Null(s.SkippedVersion);
        Assert.Empty(s.DualCameraIds);
        Assert.Equal(0, s.DualNextReplace);
    }

    [Fact]
    public void Out_of_range_dual_values_are_repaired_on_load()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "s.json");
        File.WriteAllText(path, """{ "layoutMode": "Dual", "dualCameraIds": null, "dualNextReplace": 7 }""");
        var s = new SettingsStore(path).Load();
        Assert.Equal(LayoutMode.Dual, s.LayoutMode);
        Assert.NotNull(s.DualCameraIds);
        Assert.Equal(0, s.DualNextReplace);
    }

    [Fact]
    public void Update_and_dual_settings_round_trip()
    {
        var path = Path.Combine(_dir, "rt.json");
        var id = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.FromHours(2));
        new SettingsStore(path).Save(new AppSettings
        {
            LayoutMode = LayoutMode.FeaturedLeft, DualCameraIds = [id], DualNextReplace = 1,
            CheckUpdatesOnStartup = false, LastUpdateCheck = when, SkippedVersion = "1.3.0",
        });
        var s = new SettingsStore(path).Load();
        Assert.Equal(LayoutMode.FeaturedLeft, s.LayoutMode);
        Assert.Equal([id], s.DualCameraIds);
        Assert.Equal(1, s.DualNextReplace);
        Assert.False(s.CheckUpdatesOnStartup);
        Assert.Equal(when, s.LastUpdateCheck);
        Assert.Equal("1.3.0", s.SkippedVersion);
    }

    [Fact]
    public void Sound_and_quiet_defaults_and_invalid_times_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "q.json");
        File.WriteAllText(path, """{ "quietHoursEnabled": true, "quietFrom": "25:00", "quietTo": "7" }""");
        var s = new SettingsStore(path).Load();
        Assert.True(s.QuietHoursEnabled);
        Assert.Equal(("23:00", "07:00"), (s.QuietFrom, s.QuietTo));
        Assert.False(new AppSettings().SoundOnConnectionLost);
        Assert.False(new AppSettings().SoundOnMotion);
    }

    [Theory]
    [InlineData("00:00", true)] [InlineData("23:59", true)] [InlineData("7:05", false)] [InlineData("24:00", false)] [InlineData("", false)] [InlineData(null, false)]
    public void TryParseTime_accepts_only_HH_mm(string? text, bool ok) => Assert.Equal(ok, AppSettings.TryParseTime(text, out _));
}
