using Centinela.Platform.Linux;

namespace Centinela.Platform.Tests;

public sealed class LinuxAutoStartTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "cla-" + Guid.NewGuid().ToString("N")[..8], "autostart");
    string EntryPath => Path.Combine(_dir, "centinela.desktop");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void AutoStart_content_is_exact()
    {
        Assert.Equal(
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Centinela\n" +
            "Comment=Visor de cámaras IP\n" +
            "Exec=/opt/centinela/Centinela --tray\n" +
            "Icon=centinela\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n",
            DesktopEntry.AutoStart("/opt/centinela/Centinela"));
    }

    [Fact]
    public void Plain_path_is_not_quoted()
        => Assert.Equal("/usr/lib/centinela/Centinela", DesktopEntry.Quote("/usr/lib/centinela/Centinela"));

    [Fact]
    public void Path_with_spaces_is_quoted()
        => Assert.Equal("\"/home/ana/Mis apps/Centinela\"", DesktopEntry.Quote("/home/ana/Mis apps/Centinela"));

    [Fact]
    public void Reserved_characters_are_escaped_inside_quotes_and_backslash_twice()
    {
        // Exec quoting escapes " ` $ \ with a backslash; the string value then escapes every backslash again.
        Assert.Equal("\"/a\\\\\"b\\\\`c\\\\$d\\\\\\\\e\"", DesktopEntry.Quote("/a\"b`c$d\\e"));
    }

    [Fact]
    public void Percent_is_doubled_even_without_quotes()
        => Assert.Equal("/opt/100%%/Centinela", DesktopEntry.Quote("/opt/100%/Centinela"));

    [Fact]
    public void Exec_with_spaces_in_written_file_is_quoted()
    {
        new LinuxAutoStart(_dir, "/home/ana/Mis apps/Centinela").Enable();
        Assert.Contains("\nExec=\"/home/ana/Mis apps/Centinela\" --tray\n", File.ReadAllText(EntryPath));
    }

    [Fact]
    public void Enable_then_disable()
    {
        var auto = new LinuxAutoStart(_dir, "/opt/centinela/Centinela");
        Assert.False(auto.IsEnabled);
        auto.Enable();
        Assert.True(auto.IsEnabled);
        Assert.Equal(DesktopEntry.AutoStart("/opt/centinela/Centinela"), File.ReadAllText(EntryPath));
        Assert.Single(Directory.GetFiles(_dir));
        auto.Disable();
        Assert.False(auto.IsEnabled);
        Assert.False(File.Exists(EntryPath));
        auto.Disable();
    }

    [Theory]
    [InlineData("/home/ana/Mis apps/Centinela")]
    [InlineData("/opt/it's \"x\" $HOME `y` 100% a\\b #(1)/Centinela")]
    public void Awkward_paths_round_trip(string exe)
    {
        var auto = new LinuxAutoStart(_dir, exe);
        auto.Enable();
        Assert.True(auto.IsEnabled);
        Assert.False(auto.Migrate());
    }

    [Fact]
    public void Entry_for_another_path_is_not_enabled()
    {
        new LinuxAutoStart(_dir, "/old/Centinela").Enable();
        Assert.False(new LinuxAutoStart(_dir, "/new/Centinela").IsEnabled);
    }

    [Theory]
    [InlineData("Hidden=true")]
    [InlineData("X-GNOME-Autostart-enabled=false")]
    public void Entry_disabled_by_the_desktop_is_not_enabled_nor_migrated(string line)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(EntryPath, DesktopEntry.AutoStart("/old/Centinela").Replace("X-GNOME-Autostart-enabled=true\n", "") + line + "\n");
        Assert.False(new LinuxAutoStart(_dir, "/old/Centinela").IsEnabled);
        var current = new LinuxAutoStart(_dir, "/new/Centinela");
        Assert.False(current.Migrate());
        Assert.Contains(line, File.ReadAllText(EntryPath));
    }

    [Fact]
    public void Hidden_in_another_group_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(EntryPath, DesktopEntry.AutoStart("/opt/Centinela") + "\n[Desktop Action x]\nHidden=true\nExec=/other\n");
        Assert.True(new LinuxAutoStart(_dir, "/opt/Centinela").IsEnabled);
    }

    [Fact]
    public void Migrate_rewrites_an_entry_for_another_install()
    {
        new LinuxAutoStart(_dir, "/old place/Centinela").Enable();
        var auto = new LinuxAutoStart(_dir, "/opt/centinela/Centinela");
        Assert.True(auto.Migrate());
        Assert.True(auto.IsEnabled);
        Assert.Equal(DesktopEntry.AutoStart("/opt/centinela/Centinela"), File.ReadAllText(EntryPath));
        Assert.False(auto.Migrate());
    }

    [Fact]
    public void Migrate_without_entry_does_nothing()
    {
        Assert.False(new LinuxAutoStart(_dir, "/opt/centinela/Centinela").Migrate());
        Assert.False(File.Exists(EntryPath));
    }

    [Fact]
    public void Default_directory_uses_absolute_xdg_config_home()
        => Assert.Equal("/cfg/autostart",
            LinuxAutoStart.DefaultDirectory(n => n == "XDG_CONFIG_HOME" ? "/cfg/" : null, "/home/ana"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/cfg")]
    public void Default_directory_falls_back_to_home_config(string? xdg)
        => Assert.Equal("/home/ana/.config/autostart",
            LinuxAutoStart.DefaultDirectory(n => n == "XDG_CONFIG_HOME" ? xdg : null, "/home/ana"));
}
