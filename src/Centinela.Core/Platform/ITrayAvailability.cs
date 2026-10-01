namespace Centinela.Core.Platform;

/// <summary>Whether the desktop can show a tray icon right now.</summary>
public interface ITrayAvailability
{
    bool IsAvailable { get; }
}
