using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Tray;

/// <summary>The tray texts that depend on the system (starting with the system is named differently on each).</summary>
static class TrayTexts
{
    public static string AutoStartLabel(OsKind os) => os switch
    {
        OsKind.Windows => "Arrancar con Windows",
        OsKind.MacOS => "Abrir al iniciar sesión",
        _ => "Arrancar al iniciar sesión",
    };

    public static string AutoStartUnavailable(OsKind os) =>
        $"«{AutoStartLabel(os)}» no está disponible con CENTINELA_DATA_DIR.";

    /// <summary>Changing the setting failed (the check mark is then read again).</summary>
    public static string AutoStartFailed(OsKind os, string message) => os == OsKind.Windows
        ? $"No se pudo cambiar el arranque con Windows: {message}"
        : $"No se pudo cambiar el arranque al iniciar sesión: {message}";

    /// <summary>The title of the notice the first time the window hides (macOS has a menu bar, not a tray).</summary>
    public static string HiddenTitle(OsKind os) => os == OsKind.MacOS
        ? "Centinela sigue en la barra de menús"
        : "Centinela sigue en la bandeja";

    /// <summary>
    /// The first time the window hides in the tray: Windows opens it with a double click, Linux with one; on macOS a
    /// click opens the icon's menu.
    /// </summary>
    public static string HiddenHint(OsKind os) => os switch
    {
        OsKind.Windows => "Las grabaciones continúan. Doble clic en el icono para abrirla; «Salir» la cierra.",
        OsKind.MacOS => "Las grabaciones continúan. Para volver a abrirla, usa «Abrir» en el menú del icono de la barra de menús o haz clic en Centinela en el Dock.",
        _ => "Las grabaciones continúan. Haz clic en el icono para abrirla; «Salir» la cierra.",
    };
}
