using Centinela.Core;
using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Tests;

public class AutomaticBackupTests
{
    static Camera Named(string name) => new() { Name = name, Host = name };

    [Fact]
    public async Task Requests_made_while_writing_collapse_into_one_write_of_the_latest_list()
    {
        var gate = new ManualResetEventSlim();
        var written = new List<string>();
        var backup = new AutomaticBackup((folder, cameras) =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            lock (written) written.Add(folder + ":" + string.Join(",", cameras.Select(c => c.Name)));
        });
        backup.Schedule("f", [Named("A")]);
        backup.Schedule("f", [Named("B")]);
        backup.Schedule("g", [Named("C")]);
        gate.Set();
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["f:A", "g:C"], written);
    }

    [Fact]
    public async Task The_list_is_copied_when_scheduled()
    {
        var names = new List<string>();
        var backup = new AutomaticBackup((_, cameras) => names.AddRange(cameras.Select(c => c.Name)));
        var camera = Named("A");
        backup.Schedule("f", [camera]);
        camera.Name = "cambiada";
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["A"], names);
    }

    [Fact]
    public async Task A_failed_copy_is_reported_and_the_next_one_still_runs()
    {
        var calls = 0;
        var backup = new AutomaticBackup((_, _) => { if (Interlocked.Increment(ref calls) == 1) throw new IOException("disco lleno"); });
        string? failure = null;
        backup.Failed += message => failure = message;
        backup.Schedule("f", [Named("A")]);
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        backup.Schedule("f", [Named("A")]);
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("No se pudo guardar la copia automática: disco lleno", 2), (failure, calls));
    }
}
