namespace Centinela.Core;

/// <summary>At most one audio source plays in the whole app. Not thread-safe (UI thread).</summary>
public sealed class AudioCoordinator<T> where T : class
{
    public T? Active { get; private set; }

    /// <summary>Makes <paramref name="source"/> the one that plays; returns the source that must now be silenced, if any.</summary>
    public T? Activate(T source)
    {
        if (ReferenceEquals(Active, source)) return null;
        var previous = Active;
        Active = source;
        return previous;
    }

    public bool Deactivate(T source)
    {
        if (!ReferenceEquals(Active, source)) return false;
        Active = null;
        return true;
    }
}
