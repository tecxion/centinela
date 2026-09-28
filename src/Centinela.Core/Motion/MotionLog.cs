using System.Globalization;

namespace Centinela.Core;

/// <summary>One finished motion event: when it started, on which camera, how long it lasted and its peak changed fraction (0 to 1).</summary>
public sealed record MotionLogEntry(DateTime Start, string Camera, TimeSpan Duration, double Peak);

/// <summary>In-memory ring of the latest motion events plus one append-only file per day, written off the caller's thread.</summary>
public sealed class MotionLog : IDisposable
{
    public const int Capacity = 500;
    const string Prefix = "movimiento-";

    readonly DailyLog<MotionLogEntry> _log;

    public MotionLog(string directory)
    {
        _log = new DailyLog<MotionLogEntry>(directory, Prefix, e => e.Start, FormatLine, Capacity);
    }

    public event Action<MotionLogEntry>? EntryAdded
    {
        add => _log.EntryAdded += value;
        remove => _log.EntryAdded -= value;
    }

    public void Add(MotionLogEntry entry) => _log.Add(entry);

    public IReadOnlyList<MotionLogEntry> Snapshot() => _log.Snapshot();

    public void Clear() => _log.Clear();

    public static string FileFor(string directory, DateTime time) =>
        DailyLog<MotionLogEntry>.FileFor(directory, Prefix, time);

    /// <summary><c>yyyy-MM-dd HH:mm:ss</c>, camera, duration and peak, tab-separated; e.g. fields <c>2026-09-28 10:00:05</c>, <c>Garaje</c>, <c>0:07</c>, <c>4.2 %</c>.</summary>
    public static string FormatLine(MotionLogEntry e) => string.Join('\t',
        LogText.Stamp(e.Start),
        LogText.Clean(e.Camera),
        FormatDuration(e.Duration),
        (e.Peak * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %");

    public static int PurgeOlderThan(string directory, DateTime now, int days = 14) =>
        DailyLog<MotionLogEntry>.PurgeOlderThan(directory, Prefix, now, days);

    public void Dispose() => _log.Dispose();

    /// <summary>Duration as in <see cref="FormatLine"/>: <c>m:ss</c> under an hour (<c>0:07</c>, <c>12:30</c>), <c>h:mm:ss</c> from an hour (<c>1:02:03</c>).</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        var hours = (long)duration.TotalHours;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{duration.Minutes:00}:{duration.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes}:{duration.Seconds:00}");
    }
}
