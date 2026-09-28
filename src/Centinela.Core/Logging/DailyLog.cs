using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Centinela.Core;

/// <summary>
/// In-memory ring of the latest entries (newest first) plus one append-only UTF-8 file per day
/// (<c>{prefix}yyyy-MM-dd.log</c>), written by a background task off the caller's thread.
/// </summary>
public sealed class DailyLog<T> : IDisposable where T : class
{
    readonly string _directory;
    readonly string _prefix;
    readonly Func<T, DateTime> _time;
    readonly Func<T, string> _format;
    readonly int _capacity;
    readonly object _gate = new();
    readonly LinkedList<T> _entries = new();
    readonly BlockingCollection<T> _pending = new();
    readonly Task _writer;

    public DailyLog(string directory, string prefix, Func<T, DateTime> time, Func<T, string> format, int capacity = 500)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _directory = directory;
        _prefix = prefix;
        _time = time;
        _format = format;
        _capacity = capacity;
        _writer = Task.Run(WriteLoop);
    }

    public event Action<T>? EntryAdded;

    public void Add(T entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > _capacity) _entries.RemoveLast();
        }
        // After Dispose the entry stays in memory only; CompleteAdding may race with this call.
        try { _pending.TryAdd(entry); }
        catch (InvalidOperationException) { }
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    public static string FileFor(string directory, string prefix, DateTime time) =>
        Path.Combine(directory, $"{prefix}{time:yyyy-MM-dd}.log");

    public static int PurgeOlderThan(string directory, string prefix, DateTime now, int days = 14)
    {
        if (!Directory.Exists(directory)) return 0;
        var deleted = 0;
        foreach (var file in Directory.GetFiles(directory, $"{prefix}*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[prefix.Length..];
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < now.Date.AddDays(-days))
            {
                try { File.Delete(file); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return deleted;
    }

    public void Dispose()
    {
        _pending.CompleteAdding();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* the log must never block or break shutdown */ }
    }

    void WriteLoop()
    {
        foreach (var entry in _pending.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(FileFor(_directory, _prefix, _time(entry)), _format(entry) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Losing one line is better than losing the writer: keep draining.
            }
        }
    }
}

/// <summary>Field helpers shared by the tab-separated daily log line formats.</summary>
static class LogText
{
    /// <summary>Tabs separate fields and newlines separate lines, so neither may appear inside a field.</summary>
    public static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static string Stamp(DateTime time) => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
