using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace CamaraWin.Core;

public sealed record ErrorLogEntry(DateTime Time, string Camera, string Kind, string Title, string Detail);

/// <summary>In-memory ring of the latest entries plus one append-only file per day, written off the caller's thread.</summary>
public sealed class ErrorLog : IDisposable
{
    public const int Capacity = 500;
    const string Prefix = "camarawin-";

    readonly string _directory;
    readonly object _gate = new();
    readonly LinkedList<ErrorLogEntry> _entries = new();
    readonly BlockingCollection<ErrorLogEntry> _pending = new();
    readonly Task _writer;

    public ErrorLog(string directory)
    {
        _directory = directory;
        _writer = Task.Run(WriteLoop);
    }

    public event Action<ErrorLogEntry>? EntryAdded;

    public void Add(ErrorLogEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity) _entries.RemoveLast();
        }
        // After Dispose the entry stays in memory only; CompleteAdding may race with this call.
        try { _pending.TryAdd(entry); }
        catch (InvalidOperationException) { }
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<ErrorLogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    public static string FileFor(string directory, DateTime time) =>
        Path.Combine(directory, $"{Prefix}{time:yyyy-MM-dd}.log");

    public static string FormatLine(ErrorLogEntry e) => string.Join('\t',
        e.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        Clean(e.Camera), Clean(e.Kind), Clean(e.Title), Clean(e.Detail));

    public static int PurgeOlderThan(string directory, DateTime now, int days = 14)
    {
        if (!Directory.Exists(directory)) return 0;
        var deleted = 0;
        foreach (var file in Directory.GetFiles(directory, $"{Prefix}*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
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
                File.AppendAllText(FileFor(_directory, entry.Time), FormatLine(entry) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Losing one line is better than losing the writer: keep draining.
            }
        }
    }

    static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
