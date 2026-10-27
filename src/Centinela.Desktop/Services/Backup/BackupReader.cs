using Centinela.Core;

namespace Centinela.Desktop.Services.Backup;

/// <summary>What reading a backup gave: the cameras, or an error to show; both null when cancelled.</summary>
sealed record BackupReadResult(BackupImport? Import, string? Error);

/// <summary>
/// Reads and decrypts a backup for «Importar JSON…». The passphrase and the retry question are asked on the
/// caller's thread (again after a wrong passphrase); decoding runs in the background.
/// </summary>
sealed class BackupReader(Func<Task<string?>> askPassphrase, Func<string, Task<bool>> askRetry, Action<string> status)
{
    public async Task<BackupReadResult> ReadAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var encrypted = CameraBackup.IsEncrypted(json);
            while (true)
            {
                string? passphrase = null;
                if (encrypted && (passphrase = await askPassphrase()) is null) return new(null, null);
                status(encrypted ? "Descifrando la copia…" : "Leyendo la copia…");
                try
                {
                    var imported = await Task.Run(() => CameraBackup.Import(json, () => passphrase));
                    status("");
                    return new(imported, null);
                }
                catch (BackupPassphraseException ex)
                {
                    status("");
                    if (!await askRetry($"{ex.Message} ¿Probar con otra clave?")) return new(null, null);
                }
            }
        }
        catch (OperationCanceledException)
        {
            status("");
            return new(null, null);
        }
        catch (Exception ex) when (ex is BackupFormatException or IOException or UnauthorizedAccessException)
        {
            status("");
            return new(null, ex.Message);
        }
    }
}
