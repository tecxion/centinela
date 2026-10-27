namespace Centinela.Core.Platform;

/// <summary>One Centinela per user session: a later launch wakes the first one and exits.</summary>
public interface ISingleInstance
{
    /// <summary>True for the first instance; a later instance signals the first one and gets false.</summary>
    bool TryClaim();

    /// <summary>Calls <paramref name="onActivate"/> on a background thread each time a later instance starts.</summary>
    void ListenForActivation(Action onActivate);
}
