using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Tray;

/// <summary>What the main window's X does.</summary>
public enum CloseAction { Hide, Minimize, Close }

static class WindowClose
{
    /// <summary>
    /// «Salir» or the end of the session closes. Otherwise, with a tray the X hides the window there; without one it
    /// minimizes on macOS and Linux (so recordings and detection keep running) and closes on Windows, as always.
    /// </summary>
    public static CloseAction Decide(bool exitRequested, bool systemExit, bool hasTray, OsKind os)
    {
        if (exitRequested || systemExit) return CloseAction.Close;
        if (hasTray) return CloseAction.Hide;
        return os == OsKind.Windows ? CloseAction.Close : CloseAction.Minimize;
    }
}
