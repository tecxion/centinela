using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class AutoStartTests
{
    sealed class FakeRunKey : IRunKey
    {
        public readonly Dictionary<string, string> Values = [];
        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    [Fact]
    public void Enable_writes_quoted_command_with_tray_flag_and_disable_removes_it()
    {
        var key = new FakeRunKey();
        var auto = new AutoStart(key, @"C:\Apps\CamaraWin\CamaraWin.exe");
        Assert.False(auto.IsEnabled);
        auto.Enable();
        Assert.Equal("\"C:\\Apps\\CamaraWin\\CamaraWin.exe\" --tray", key.Values["CamaraWin"]);
        Assert.True(auto.IsEnabled);
        auto.Disable();
        Assert.False(auto.IsEnabled);
        Assert.Empty(key.Values);
    }

    [Fact]
    public void Entry_for_another_path_is_not_enabled()
    {
        var key = new FakeRunKey();
        key.Set("CamaraWin", "\"D:\\old\\CamaraWin.exe\" --tray");
        Assert.False(new AutoStart(key, @"C:\new\CamaraWin.exe").IsEnabled);
    }
}
