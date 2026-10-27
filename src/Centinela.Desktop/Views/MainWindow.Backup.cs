using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Centinela.Core;
using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Views;

/// <summary>The Archivo menu's copies: «Importar JSON…», «Exportar JSON…» and «Carpeta de copia automática…».</summary>
public partial class MainWindow
{
    static readonly FilePickerFileType[] BackupFileTypes = [new("Copia de Centinela") { Patterns = ["*.json"] }];

    // Import/export derive keys with PBKDF2 off the UI thread; one operation at a time.
    bool _backupBusy;
    // The latest manual export (only one runs at a time); the exit wait includes it.
    Task _manualExport = Task.CompletedTask;

    async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        _backupBusy = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar",
                FileTypeFilter = BackupFileTypes,
                SuggestedStartLocation = await BackupStartAsync(),
            });
            if (_closing || files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var reader = new BackupReader(
                // Closing, or hidden in the tray meanwhile: no dialog owned by a hidden window; the questions cancel.
                () => _closing || IsHiddenInTray ? Task.FromResult<string?>(null) : PassphraseDialog.AskAsync(this, confirm: false),
                question => _closing || IsHiddenInTray ? Task.FromResult(false) : MessageDialog.ConfirmAsync(this, question, title: "Importar"),
                message => _vm.Notify(message, null));
            var result = await reader.ReadAsync(path);
            if (_closing) return;
            if (result.Error is { } error)
            {
                await ShowBackupMessageAsync(error, "Importar");
                return;
            }
            if (result.Import is not { } imported) return;
            // Updated cameras may have new URLs or credentials: fresh notices and new sessions.
            var summary = _vm.ApplyImport(imported, id =>
            {
                _errors.Forget(id);
                DisposeTilesOf(id);
            });
            // New, updated or re-enabled cameras: detection follows them.
            ApplyMotion();
            if (IsHiddenInTray)
            {
                // Hidden in the tray (the X during the read): no dialog owned by a hidden window, as in the WPF.
                _vm.Notify($"Importación terminada: {summary}", null);
                ShowSystemNotice("Importar", summary);
                return;
            }
            await MessageDialog.ShowAsync(this, summary, "Importar");
        }
        finally
        {
            _backupBusy = false;
        }
    }

    async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        _backupBusy = true;
        try
        {
            var options = new ExportDialog();
            if (!await options.ShowDialog<bool>(this) || _closing) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar",
                SuggestedFileName = $"centinela-camaras-{DateTime.Now:yyyy-MM-dd}.json",
                DefaultExtension = "json",
                FileTypeChoices = BackupFileTypes,
                SuggestedStartLocation = await BackupStartAsync(),
            });
            if (_closing || file?.TryGetLocalPath() is not { } path) return;
            var snapshot = _vm.Cameras.All.Select(c => c.Clone()).ToList();
            var passphrase = options.IncludePasswords ? options.Passphrase : null;
            _vm.Notify(passphrase is null ? "Exportando…" : "Cifrando la copia…", null);
            try
            {
                // Atomic, and tracked so that exiting meanwhile waits for it instead of cutting the file short.
                var export = Task.Run(() => BackupWriter.WriteAtomic(path, CameraBackup.Export(snapshot, passphrase)));
                _manualExport = export;
                await export;
                if (_closing) return;
                _vm.Notify($"Copia exportada: {path}", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (_closing) return;
                _vm.Notify("", null);
                await ShowBackupMessageAsync(ex.Message, "Exportar");
            }
        }
        finally
        {
            _backupBusy = false;
        }
    }

    async void BackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Carpeta de copia automática",
            SuggestedStartLocation = await BackupStartAsync(),
        });
        if (_closing || folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        _vm.SetBackupFolder(path);
    }

    /// <summary>A failed import or export: a dialog, or, hidden in the tray, the status line and a system notice.</summary>
    async Task ShowBackupMessageAsync(string message, string title)
    {
        if (IsHiddenInTray)
        {
            _vm.Notify(message, null);
            ShowSystemNotice(title, message);
            return;
        }
        await MessageDialog.ShowAsync(this, message, title);
    }

    /// <summary>The backup folder for the pickers; null when it does not exist yet (or the setting is malformed).</summary>
    async Task<IStorageFolder?> BackupStartAsync()
    {
        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(_vm.BackupFolder);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
