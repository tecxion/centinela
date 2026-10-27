using Centinela.Platform.MacOS;

namespace Centinela.Platform.Tests;

public sealed class MacAutoStartTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "cma-" + Guid.NewGuid().ToString("N")[..8], "LaunchAgents");
    string AgentPath => Path.Combine(_dir, "es.tecxart.centinela.plist");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    static string WithDisabled(string plist, string value) =>
        plist.Replace("</dict>", $"\t<key>Disabled</key>\n\t<{value}/>\n</dict>");

    [Fact]
    public void Agent_content_is_exact()
    {
        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "\t<key>Label</key>\n" +
            "\t<string>es.tecxart.centinela</string>\n" +
            "\t<key>ProgramArguments</key>\n" +
            "\t<array>\n" +
            "\t\t<string>/Applications/Centinela.app/Contents/MacOS/Centinela</string>\n" +
            "\t\t<string>--tray</string>\n" +
            "\t</array>\n" +
            "\t<key>RunAtLoad</key>\n" +
            "\t<true/>\n" +
            "</dict>\n" +
            "</plist>\n",
            LaunchAgent.For("/Applications/Centinela.app/Contents/MacOS/Centinela"));
    }

    [Fact]
    public void Path_with_ampersand_and_spaces_is_escaped_and_read_back()
    {
        const string exe = "/Users/ana/Mis & apps/Centinela";
        var plist = LaunchAgent.For(exe);
        Assert.Contains("\t\t<string>/Users/ana/Mis &amp; apps/Centinela</string>\n", plist);
        Assert.Equal(exe, LaunchAgent.ProgramOf(plist));
    }

    [Theory]
    [InlineData("/Users/ana/Mis apps/Centinela")]
    [InlineData("/Users/ana/A&B <x> it's \"y\"/Centinela")]
    public void Awkward_paths_round_trip(string exe)
    {
        var auto = new MacAutoStart(_dir, exe);
        auto.Enable();
        Assert.True(auto.IsEnabled);
        Assert.False(auto.Migrate());
    }

    [Fact]
    public void Enable_then_disable()
    {
        var auto = new MacAutoStart(_dir, "/Applications/Centinela.app/Contents/MacOS/Centinela");
        Assert.False(auto.IsEnabled);
        auto.Enable();
        Assert.True(auto.IsEnabled);
        Assert.Equal(LaunchAgent.For("/Applications/Centinela.app/Contents/MacOS/Centinela"), File.ReadAllText(AgentPath));
        Assert.Equal((byte)'<', File.ReadAllBytes(AgentPath)[0]); // No BOM.
        Assert.Single(Directory.GetFiles(_dir));
        auto.Disable();
        Assert.False(auto.IsEnabled);
        Assert.False(File.Exists(AgentPath));
        auto.Disable();
    }

    [Fact]
    public void Agent_for_another_path_is_not_enabled()
    {
        new MacAutoStart(_dir, "/old/Centinela").Enable();
        Assert.False(new MacAutoStart(_dir, "/new/Centinela").IsEnabled);
    }

    [Fact]
    public void Disabled_agent_is_not_enabled_nor_migrated()
    {
        Directory.CreateDirectory(_dir);
        var plist = WithDisabled(LaunchAgent.For("/old/Centinela"), "true");
        File.WriteAllText(AgentPath, plist);
        Assert.True(LaunchAgent.IsDisabled(plist));
        Assert.False(new MacAutoStart(_dir, "/old/Centinela").IsEnabled);
        Assert.False(new MacAutoStart(_dir, "/new/Centinela").Migrate());
        Assert.Equal(plist, File.ReadAllText(AgentPath));
    }

    [Fact]
    public void Disabled_false_is_still_enabled()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(AgentPath, WithDisabled(LaunchAgent.For("/opt/Centinela"), "false"));
        Assert.True(new MacAutoStart(_dir, "/opt/Centinela").IsEnabled);
    }

    [Fact]
    public void Migrate_rewrites_an_agent_for_another_install()
    {
        new MacAutoStart(_dir, "/Volumes/Old place/Centinela").Enable();
        var auto = new MacAutoStart(_dir, "/Applications/Centinela.app/Contents/MacOS/Centinela");
        Assert.True(auto.Migrate());
        Assert.True(auto.IsEnabled);
        Assert.Equal(LaunchAgent.For("/Applications/Centinela.app/Contents/MacOS/Centinela"), File.ReadAllText(AgentPath));
        Assert.False(auto.Migrate());
    }

    [Fact]
    public void Migrate_without_agent_does_nothing()
    {
        Assert.False(new MacAutoStart(_dir, "/opt/Centinela").Migrate());
        Assert.False(File.Exists(AgentPath));
    }

    [Theory]
    [InlineData("not a plist")]
    [InlineData("")]
    [InlineData("<plist version=\"1.0\"><dict><key>ProgramArguments</key>")]
    public void Unreadable_agent_is_not_enabled_nor_migrated(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(AgentPath, text);
        Assert.Null(LaunchAgent.ProgramOf(text));
        Assert.False(LaunchAgent.IsDisabled(text));
        Assert.False(new MacAutoStart(_dir, "/opt/Centinela").IsEnabled);
        Assert.False(new MacAutoStart(_dir, "/opt/Centinela").Migrate());
        Assert.Equal(text, File.ReadAllText(AgentPath));
    }

    [Fact]
    public void Default_directory_is_the_user_launch_agents()
        => Assert.Equal("/Users/ana/Library/LaunchAgents", MacAutoStart.DefaultDirectory("/Users/ana"));
}
