using Centinela.Core;

namespace Centinela.Core.Tests;

public class LegacyDataTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "centinela-legacy-" + Guid.NewGuid());
    string Old => Path.Combine(_root, "CamaraWin");
    string New => Path.Combine(_root, "Centinela");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void First_run_copies_cameras_and_settings_and_keeps_the_old_folder()
    {
        Directory.CreateDirectory(Old);
        File.WriteAllText(Path.Combine(Old, "cameras.json"), "cams");
        File.WriteAllText(Path.Combine(Old, "settings.json"), "set");

        Assert.True(LegacyData.Migrate(Old, New));

        Assert.Equal("cams", File.ReadAllText(Path.Combine(New, "cameras.json")));
        Assert.Equal("set", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Old, "cameras.json")));
    }

    [Fact]
    public void Existing_new_data_is_never_overwritten()
    {
        Directory.CreateDirectory(Old);
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(Old, "cameras.json"), "old");
        File.WriteAllText(Path.Combine(New, "cameras.json"), "new");

        Assert.False(LegacyData.Migrate(Old, New));
        Assert.Equal("new", File.ReadAllText(Path.Combine(New, "cameras.json")));
    }

    [Fact]
    public void Nothing_to_migrate_creates_nothing()
    {
        Assert.False(LegacyData.Migrate(Old, New));
        Assert.False(Directory.Exists(New));
    }
}
