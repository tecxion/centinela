using Centinela.Core.Platform;
using Centinela.Desktop.Services.Tray;

namespace Centinela.Desktop.Tests;

public class TrayMenuTests
{
    sealed class Calls
    {
        public readonly List<string> Log = [];
        public Action Open => () => Log.Add("open");
        public Action RecordAll => () => Log.Add("record");
        public Action<bool> SetAutoStart => on => Log.Add($"autostart:{on}");
        public Action Exit => () => Log.Add("exit");
    }

    static IReadOnlyList<TrayMenuItem> Build(Calls calls, bool recording = false, bool available = true,
        bool enabled = false, OsKind os = OsKind.Windows) =>
        TrayMenu.Build(recording, available, enabled, os, calls.Open, calls.RecordAll, calls.SetAutoStart, calls.Exit);

    [Fact]
    public void Items_and_order_when_nothing_records()
    {
        var items = Build(new Calls());
        Assert.Equal(["Abrir", "Grabar todas", "Arrancar con Windows", null, "Salir"], items.Select(i => i.Text));
        Assert.Null(items[3].OnClick);
    }

    [Fact]
    public void Record_all_turns_into_stop_all_while_recording()
    {
        var items = Build(new Calls(), recording: true);
        Assert.Equal("Detener todas", items[1].Text);
    }

    [Fact]
    public void Without_auto_start_there_is_no_check_item()
    {
        var items = Build(new Calls(), available: false, enabled: true);
        Assert.Equal(["Abrir", "Grabar todas", null, "Salir"], items.Select(i => i.Text));
        Assert.DoesNotContain(items, i => i.Checkable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_auto_start_item_is_a_check_with_its_state(bool enabled)
    {
        var item = Build(new Calls(), enabled: enabled)[2];
        Assert.True(item.Checkable);
        Assert.Equal(enabled, item.Checked);
        Assert.False(Build(new Calls())[0].Checkable);
    }

    [Fact]
    public void The_auto_start_item_uses_the_label_of_the_system()
    {
        Assert.Equal("Abrir al iniciar sesión", Build(new Calls(), os: OsKind.MacOS)[2].Text);
        Assert.Equal("Arrancar al iniciar sesión", Build(new Calls(), os: OsKind.Linux)[2].Text);
    }

    [Fact]
    public void Each_item_calls_its_action()
    {
        var calls = new Calls();
        foreach (var item in Build(calls, enabled: true)) item.OnClick?.Invoke();
        // The check asks for the opposite of its current state.
        Assert.Equal(["open", "record", "autostart:False", "exit"], calls.Log);

        calls.Log.Clear();
        Build(calls, enabled: false)[2].OnClick!();
        Assert.Equal(["autostart:True"], calls.Log);
    }

    [Theory]
    [InlineData(OsKind.Windows, "Arrancar con Windows")]
    [InlineData(OsKind.MacOS, "Abrir al iniciar sesión")]
    [InlineData(OsKind.Linux, "Arrancar al iniciar sesión")]
    public void Texts_follow_the_system(OsKind os, string label)
    {
        Assert.Equal(label, TrayTexts.AutoStartLabel(os));
        Assert.Equal($"«{label}» no está disponible con CENTINELA_DATA_DIR.", TrayTexts.AutoStartUnavailable(os));
    }

    [Fact]
    public void Auto_start_failure_names_the_system()
    {
        Assert.Equal("No se pudo cambiar el arranque con Windows: denegado",
            TrayTexts.AutoStartFailed(OsKind.Windows, "denegado"));
        Assert.Equal("No se pudo cambiar el arranque al iniciar sesión: denegado",
            TrayTexts.AutoStartFailed(OsKind.MacOS, "denegado"));
        Assert.Equal("No se pudo cambiar el arranque al iniciar sesión: denegado",
            TrayTexts.AutoStartFailed(OsKind.Linux, "denegado"));
    }
}
