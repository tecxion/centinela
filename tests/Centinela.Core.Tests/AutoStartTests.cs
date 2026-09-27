using Centinela.Core;

namespace Centinela.Core.Tests;

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
        var auto = new AutoStart(key, @"C:\Apps\Centinela\Centinela.exe");
        Assert.False(auto.IsEnabled);
        auto.Enable();
        Assert.Equal("\"C:\\Apps\\Centinela\\Centinela.exe\" --tray", key.Values["Centinela"]);
        Assert.True(auto.IsEnabled);
        auto.Disable();
        Assert.False(auto.IsEnabled);
        Assert.Empty(key.Values);
    }

    [Fact]
    public void Entry_for_another_path_is_not_enabled()
    {
        var key = new FakeRunKey();
        key.Set("Centinela", "\"D:\\old\\Centinela.exe\" --tray");
        Assert.False(new AutoStart(key, @"C:\new\Centinela.exe").IsEnabled);
    }

    [Fact]
    public void Pre_rename_entry_is_replaced_by_one_for_this_exe()
    {
        var key = new FakeRunKey();
        key.Set("CamaraWin", "\"D:\\old\\CamaraWin.exe\" --tray");
        var auto = new AutoStart(key, @"C:\new\Centinela.exe");
        Assert.True(auto.MigrateLegacy());
        Assert.False(key.Values.ContainsKey("CamaraWin"));
        Assert.True(auto.IsEnabled);
        Assert.False(auto.MigrateLegacy());
    }

    [Fact]
    public void Disable_also_removes_the_pre_rename_entry()
    {
        var key = new FakeRunKey();
        key.Set("CamaraWin", "x");
        new AutoStart(key, @"C:\new\Centinela.exe").Disable();
        Assert.Empty(key.Values);
    }
}
