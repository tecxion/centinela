namespace Centinela.Core.Platform;

/// <summary>Starts Centinela hidden in the tray (<c>--tray</c>) when the user logs in.</summary>
public interface IAutoStart
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();

    /// <summary>Repairs an entry left by an older version (e.g. the CamaraWin name). True when it changed something.</summary>
    bool Migrate();
}
