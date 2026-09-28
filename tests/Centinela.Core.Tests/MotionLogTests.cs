using Centinela.Core;

namespace Centinela.Core.Tests;

public sealed class MotionLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-motionlog-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Format_line()
    {
        Assert.Equal("2026-09-28 10:00:05\tGar aje\t0:07\t4.2 %",
            MotionLog.FormatLine(new MotionLogEntry(new DateTime(2026, 9, 28, 10, 0, 5), "Gar\taje", TimeSpan.FromSeconds(7), 0.0421)));
        Assert.Equal("2026-09-28 10:00:05\tA\t1:02:03\t60.0 %",
            MotionLog.FormatLine(new MotionLogEntry(new DateTime(2026, 9, 28, 10, 0, 5), "A", new TimeSpan(1, 2, 3), 0.6)));
    }

    [Fact]
    public void Writes_daily_file_with_its_own_prefix_and_keeps_newest_first()
    {
        var t = new DateTime(2026, 9, 28, 10, 0, 0);
        using (var log = new MotionLog(_dir))
        {
            log.Add(new MotionLogEntry(t, "A", TimeSpan.FromSeconds(3), 0.01));
            log.Add(new MotionLogEntry(t.AddMinutes(1), "B", TimeSpan.FromSeconds(4), 0.02));
            Assert.Equal("B", log.Snapshot()[0].Camera);
        }
        Assert.EndsWith("movimiento-2026-09-28.log", MotionLog.FileFor(_dir, t));
        Assert.Equal(2, File.ReadAllLines(MotionLog.FileFor(_dir, t)).Length);
    }

    [Fact]
    public void Purge_only_touches_motion_files()
    {
        Directory.CreateDirectory(_dir);
        var now = new DateTime(2026, 9, 28);
        File.WriteAllText(MotionLog.FileFor(_dir, now.AddDays(-20)), "old");
        File.WriteAllText(ErrorLog.FileFor(_dir, now.AddDays(-20)), "old error");
        Assert.Equal(1, MotionLog.PurgeOlderThan(_dir, now));
        Assert.True(File.Exists(ErrorLog.FileFor(_dir, now.AddDays(-20))));
    }
}
