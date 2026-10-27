namespace Centinela.Platform.MacOS;

/// <summary>
/// Click actions by notification identifier, thread-safe. Past <c>capacity</c> the oldest is dropped: notifications
/// nobody clicks do not pile up.
/// </summary>
internal sealed class NoticeActions(int capacity = 50)
{
    readonly Lock _gate = new();
    readonly OrderedDictionary<string, Action> _actions = [];

    /// <summary>Keeps <paramref name="onClick"/> under a new identifier.</summary>
    public string Add(Action onClick)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _actions.Add(id, onClick);
            if (_actions.Count > capacity) _actions.RemoveAt(0);
        }
        return id;
    }

    /// <summary>The action for <paramref name="id"/>, once: it is removed.</summary>
    public Action? Take(string id)
    {
        lock (_gate) return _actions.Remove(id, out var action) ? action : null;
    }
}
