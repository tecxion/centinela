namespace Centinela.Core;

public sealed record ErrorLogEntry(DateTime Time, string Camera, string Kind, string Title, string Detail);

/// <summary>In-memory ring of the latest entries plus one append-only file per day, written off the caller's thread.</summary>
public sealed class ErrorLog : IDisposable
{
    public const int Capacity = 500;
    const string Prefix = "centinela-";

    readonly DailyLog<ErrorLogEntry> _log;

    public ErrorLog(string directory)
    {
        _log = new DailyLog<ErrorLogEntry>(directory, Prefix, e => e.Time, FormatLine, Capacity);
    }

    public event Action<ErrorLogEntry>? EntryAdded
    {
        add => _log.EntryAdded += value;
        remove => _log.EntryAdded -= value;
    }

    public void Add(ErrorLogEntry entry) => _log.Add(entry);

    public IReadOnlyList<ErrorLogEntry> Snapshot() => _log.Snapshot();

    public void Clear() => _log.Clear();

    public static string FileFor(string directory, DateTime time) =>
        DailyLog<ErrorLogEntry>.FileFor(directory, Prefix, time);

    public static string FormatLine(ErrorLogEntry e) => string.Join('\t',
        LogText.Stamp(e.Time),
        LogText.Clean(e.Camera), LogText.Clean(e.Kind), LogText.Clean(e.Title), LogText.Clean(e.Detail));

    public static int PurgeOlderThan(string directory, DateTime now, int days = 14) =>
        DailyLog<ErrorLogEntry>.PurgeOlderThan(directory, Prefix, now, days);

    public void Dispose() => _log.Dispose();
}
