namespace Centinela.Desktop.Services.Backup;

/// <summary>The passphrase checks of «Clave de la copia» and «Exportar cámaras».</summary>
static class PassphraseRules
{
    public const int MinimumLength = 8;

    /// <summary>The error to show, or null when valid. <paramref name="second"/> is null when there is no confirmation.</summary>
    public static string? Validate(string first, string? second)
    {
        if (second is not null && first.Length < MinimumLength) return $"La clave debe tener al menos {MinimumLength} caracteres.";
        if (second is not null && first != second) return "Las claves no coinciden.";
        if (first.Length == 0) return "Escribe la clave.";
        return null;
    }
}
