using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public sealed class ErrorLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-log-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static ErrorLogEntry Entry(int i, DateTime t) => new(t, $"Cam{i}", "Sin conexión", $"Cam{i}: no se puede conectar", $"detail {i}");

    [Fact]
    public void Keeps_newest_first_and_caps_capacity()
    {
        using var log = new ErrorLog(_dir);
        var t = new DateTime(2026, 9, 26, 10, 0, 0);
        for (var i = 0; i < ErrorLog.Capacity + 1; i++) log.Add(Entry(i, t));
        var snap = log.Snapshot();
        Assert.Equal(ErrorLog.Capacity, snap.Count);
        Assert.Equal("Cam500", snap[0].Camera);
    }

    [Fact]
    public void Writes_daily_file_after_dispose()
    {
        var t = new DateTime(2026, 9, 26, 10, 0, 0);
        using (var log = new ErrorLog(_dir)) log.Add(Entry(1, t));
        var text = File.ReadAllText(ErrorLog.FileFor(_dir, t));
        Assert.Contains("Cam1\tSin conexión\tCam1: no se puede conectar\tdetail 1", text);
    }

    [Fact]
    public void FormatLine_strips_tabs_and_newlines() =>
        Assert.Equal("2026-09-26 10:00:00\ta b\tk\tt\td e",
            ErrorLog.FormatLine(new ErrorLogEntry(new DateTime(2026, 9, 26, 10, 0, 0), "a\tb", "k", "t", "d\ne")));

    [Fact]
    public void Purge_deletes_only_old_log_files()
    {
        Directory.CreateDirectory(_dir);
        var now = new DateTime(2026, 9, 26);
        File.WriteAllText(ErrorLog.FileFor(_dir, now.AddDays(-20)), "old");
        File.WriteAllText(ErrorLog.FileFor(_dir, now.AddDays(-3)), "recent");
        File.WriteAllText(Path.Combine(_dir, "other.txt"), "keep");
        Assert.Equal(1, ErrorLog.PurgeOlderThan(_dir, now));
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void EntryAdded_fires_and_Clear_empties_memory_only()
    {
        using var log = new ErrorLog(_dir);
        ErrorLogEntry? seen = null;
        log.EntryAdded += e => seen = e;
        log.Add(Entry(1, DateTime.Now));
        Assert.NotNull(seen);
        log.Clear();
        Assert.Empty(log.Snapshot());
    }
}
