using Centinela.Core;

namespace Centinela.Desktop.Services.Data;

/// <summary>
/// Told once: without a keychain (Linux), passwords are kept in Centinela's encrypted file. Informative only.
/// </summary>
static class FileSecretNotice
{
    public const string Title = "Contraseñas";
    public const string Text = "Las contraseñas se guardan cifradas en un archivo accesible solo por tu usuario.";

    /// <summary>True the first time the passwords use the file; it then marks the setting (the caller saves it).</summary>
    public static bool TryClaim(bool usesFileFallback, AppSettings settings)
    {
        if (!usesFileFallback || settings.FileSecretNoticeShown) return false;
        settings.FileSecretNoticeShown = true;
        return true;
    }
}
