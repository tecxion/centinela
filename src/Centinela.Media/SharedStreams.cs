namespace Centinela.Media;

/// <summary>
/// Hands out leases on one shared <see cref="StreamSession"/> per (key, url, useUdp). The session decodes at the
/// largest size requested by its live leases and is stopped, off the calling thread, when the last lease is released.
/// </summary>
public sealed class SharedStreams : IDisposable
{
    readonly object _gate = new();
    readonly Dictionary<(Guid Key, string Url, bool UseUdp), Entry> _entries = [];
    bool _disposed;

    internal sealed class Entry((Guid, string, bool) id, StreamSession session)
    {
        public (Guid, string, bool) Id { get; } = id;
        public StreamSession Session { get; } = session;
        public List<SharedStreamLease> Leases { get; } = [];
        public Task? StopTask;   // set once, under the gate, when the entry is closed
    }

    /// <summary>
    /// A lease on the session for (key, url, useUdp). The first lease creates it, calls onCreated (to wire
    /// events once) and starts it; the last release stops it off the calling thread. width/height 0 = native size
    /// (see <see cref="SharedStreamLease.SetTargetSize"/>). If onCreated throws, that lease is released and the
    /// exception propagates; leases that joined meanwhile keep a started session.
    /// </summary>
    public SharedStreamLease Acquire(Guid key, string url, bool useUdp, int width, int height, Action<StreamSession>? onCreated = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        Entry entry;
        SharedStreamLease lease;
        bool created;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = (key, url, useUdp);
            created = !_entries.TryGetValue(id, out entry!);
            if (created)
            {
                entry = new Entry(id, new StreamSession(url, useUdp));
                _entries.Add(id, entry);
            }
            lease = new SharedStreamLease(this, entry, width, height);
            entry.Leases.Add(lease);
            ApplyTargetSize(entry);
        }
        if (!created) return lease;

        // Wiring runs outside the gate so handlers may call back into this object; the session is not started
        // yet, so no event can be missed.
        try
        {
            onCreated?.Invoke(entry.Session);
        }
        catch
        {
            // Leases that joined while onCreated ran still need a started session.
            _ = lease.ReleaseAsync();
            StartUnlessClosed(entry);
            throw;
        }
        StartUnlessClosed(entry);
        return lease;
    }

    void StartUnlessClosed(Entry entry)
    {
        lock (_gate)
        {
            // Every lease may already have been released (or the hub disposed) while onCreated ran.
            if (entry.StopTask is null) entry.Session.Start();
        }
    }

    /// <summary>Live sessions (tests).</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>Stops every session (app exit): returns after RequestStop, disposal runs off-thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _entries.Values) Close(entry);
            _entries.Clear();
        }
    }

    internal void SetTargetSize(SharedStreamLease lease, int width, int height)
    {
        lock (_gate)
        {
            lease.Width = width;
            lease.Height = height;
            if (lease.ReleaseTask is null) ApplyTargetSize(lease.Entry);
        }
    }

    internal Task Release(SharedStreamLease lease)
    {
        lock (_gate)
        {
            if (lease.ReleaseTask is not null) return lease.ReleaseTask;
            var entry = lease.Entry;
            entry.Leases.Remove(lease);
            if (entry.StopTask is not null)
                lease.ReleaseTask = entry.StopTask;          // already closed by Dispose
            else if (entry.Leases.Count > 0)
            {
                ApplyTargetSize(entry);
                lease.ReleaseTask = Task.CompletedTask;
            }
            else
            {
                _entries.Remove(entry.Id);
                lease.ReleaseTask = Close(entry);
            }
            return lease.ReleaseTask;
        }
    }

    // Caller holds the gate.
    static Task Close(Entry entry)
    {
        if (entry.StopTask is not null) return entry.StopTask;
        var session = entry.Session;
        session.RequestStop();
        return entry.StopTask = Task.Run(session.Dispose);
    }

    // Caller holds the gate. Max over live leases; a lease asking for 0 (native) in either dimension makes the
    // whole entry decode at native size, since no box can be larger than native.
    static void ApplyTargetSize(Entry entry)
    {
        int width = 0, height = 0;
        foreach (var lease in entry.Leases)
        {
            if (lease.Width <= 0 || lease.Height <= 0) { width = height = 0; break; }
            width = Math.Max(width, lease.Width);
            height = Math.Max(height, lease.Height);
        }
        entry.Session.SetTargetSize(width, height);
    }
}

public sealed class SharedStreamLease : IDisposable
{
    readonly SharedStreams _owner;

    internal SharedStreamLease(SharedStreams owner, SharedStreams.Entry entry, int width, int height)
    {
        _owner = owner;
        Entry = entry;
        Width = width;
        Height = height;
    }

    internal SharedStreams.Entry Entry { get; }
    internal int Width { get; set; }          // guarded by the owner's gate
    internal int Height { get; set; }
    internal Task? ReleaseTask { get; set; }

    public StreamSession Session => Entry.Session;

    /// <summary>
    /// The session decodes at the maximum size over its live leases. 0 in either dimension means native size, and
    /// then the whole shared session decodes at native size while this lease is live.
    /// </summary>
    public void SetTargetSize(int width, int height) => _owner.SetTargetSize(this, width, height);

    /// <summary>Idempotent; completes when the session is disposed if this was the last lease, else at once.</summary>
    public Task ReleaseAsync() => _owner.Release(this);

    public void Dispose() => _ = ReleaseAsync();
}
