using System.IO;
using System.Windows;
using CamaraWin.Core;
using Microsoft.Win32;

namespace CamaraWin.App;

/// <summary>The Archivo menu: JSON import/export, the automatic copy and the info windows.</summary>
public partial class MainWindow
{
    // Import/export derive keys with PBKDF2 off the UI thread; one operation at a time.
    bool _backupBusy;

    // Automatic copies: one writer at a time and only the latest camera list is written.
    readonly Lock _automaticBackupLock = new();
    (string Folder, List<Camera> Cameras)? _pendingAutomaticBackup;
    bool _automaticBackupRunning;
    Task _automaticBackupWorker = Task.CompletedTask;
    // The latest manual export (only one runs at a time); the exit wait includes it.
    Task _manualExport = Task.CompletedTask;

    string BackupFolder => string.IsNullOrWhiteSpace(_settings.BackupFolder) ? AppPaths.DefaultBackupDirectory : _settings.BackupFolder;

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        var dialog = new OpenFileDialog { Filter = "Copia de CamaraWin (*.json)|*.json", InitialDirectory = BackupFolder };
        if (dialog.ShowDialog(this) != true) return;
        _backupBusy = true;
        try
        {
            if (await ReadBackupAsync(dialog.FileName) is not { } imported || _closed) return;
            ApplyImport(imported);
        }
        finally
        {
            _backupBusy = false;
        }
    }

    /// <summary>
    /// Reads and decrypts a backup. The passphrase is asked on the UI thread (again after a wrong one);
    /// decoding runs in the background. Returns null when cancelled or after reporting an error.
    /// </summary>
    async Task<BackupImport?> ReadBackupAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var encrypted = CameraBackup.IsEncrypted(json);
            while (true)
            {
                string? passphrase = null;
                if (encrypted && (passphrase = PassphraseDialog.Ask(this, confirm: false)) is null) return null;
                Notify(encrypted ? "Descifrando la copia…" : "Leyendo la copia…", null);
                try
                {
                    var imported = await Task.Run(() => CameraBackup.Import(json, () => passphrase));
                    Notify("", null);
                    return imported;
                }
                catch (BackupPassphraseException ex)
                {
                    Notify("", null);
                    if (MessageBox.Show(this, $"{ex.Message} ¿Probar con otra clave?", "Importar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Notify("", null);
            return null;
        }
        catch (Exception ex) when (ex is BackupFormatException or IOException or UnauthorizedAccessException)
        {
            Notify("", null);
            MessageBox.Show(this, ex.Message, "Importar", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    void ApplyImport(BackupImport imported)
    {
        var merge = BackupMerge.Merge(_cameras, imported.Cameras);
        foreach (var id in merge.UpdatedIds)
        {
            // Updated cameras may point at new URLs or credentials: never keep the old sessions.
            if (_recordings.IsRecording(id)) _recordings.StopWithNotice(id);
            _errors.Forget(id);
            DisposeTilesOf(id);
        }
        _cameras.Clear();
        _cameras.AddRange(merge.Cameras);
        SaveCameras();
        var summary = $"{merge.Added} añadidas, {merge.Updated} actualizadas, {merge.WithoutPassword} sin contraseña.";
        if (IsHiddenInTray)
        {
            // Hidden in the tray: no live tiles (ShowFromTray rebuilds the view) and no dialog owned by a hidden window.
            Notify($"Importación terminada: {summary}", null);
            _tray.ShowBalloon("Importar", summary);
            return;
        }
        RebuildView();
        MessageBox.Show(this, summary, "Importar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy) return;
        var options = new ExportDialog { Owner = this };
        if (options.ShowDialog() != true) return;
        var save = new SaveFileDialog
        {
            Filter = "Copia de CamaraWin (*.json)|*.json",
            FileName = $"camarawin-camaras-{DateTime.Now:yyyy-MM-dd}.json",
            InitialDirectory = BackupFolder,
        };
        if (save.ShowDialog(this) != true) return;
        var snapshot = _cameras.Select(c => c.Clone()).ToList();
        var passphrase = options.IncludePasswords ? options.Passphrase : null;
        var path = save.FileName;
        _backupBusy = true;
        Notify(passphrase is null ? "Exportando…" : "Cifrando la copia…", null);
        try
        {
            // Atomic, and tracked so that exiting meanwhile waits for it instead of cutting the file short.
            var export = Task.Run(() => BackupWriter.WriteAtomic(path, CameraBackup.Export(snapshot, passphrase)));
            _manualExport = export;
            await export;
            if (_closed) return;
            Notify($"Copia exportada: {path}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_closed) return;
            Notify("", null);
            MessageBox.Show(this, ex.Message, "Exportar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _backupBusy = false;
        }
    }

    void BackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = BackupFolder, Title = "Carpeta de copia automática" };
        if (dialog.ShowDialog(this) != true) return;
        _settings.BackupFolder = dialog.FolderName;
        SaveSettingsQuietly();
        ScheduleAutomaticBackup();
    }

    /// <summary>
    /// Password-less copy after every camera-list change, off the UI thread. Requests made while a copy is
    /// being written collapse into one more write of the latest list. Call on the UI thread.
    /// </summary>
    void ScheduleAutomaticBackup()
    {
        var request = (BackupFolder, _cameras.Select(c => c.Clone()).ToList());
        lock (_automaticBackupLock)
        {
            _pendingAutomaticBackup = request;
            if (_automaticBackupRunning) return;
            _automaticBackupRunning = true;
            _automaticBackupWorker = Task.Run(WriteAutomaticBackups);
        }
    }

    void WriteAutomaticBackups()
    {
        while (true)
        {
            (string Folder, List<Camera> Cameras) request;
            lock (_automaticBackupLock)
            {
                if (_pendingAutomaticBackup is not { } pending)
                {
                    _automaticBackupRunning = false;
                    return;
                }
                request = pending;
                _pendingAutomaticBackup = null;
            }
            try
            {
                BackupWriter.WriteAutomatic(request.Folder, request.Cameras);
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => Notify($"No se pudo guardar la copia automática: {ex.Message}", null));
            }
        }
    }

    /// <summary>Completes when no automatic copy is being written or waiting (for the exit wait).</summary>
    Task AutomaticBackupAsync()
    {
        lock (_automaticBackupLock) return _automaticBackupWorker;
    }

    void Manual_Click(object sender, RoutedEventArgs e) => new InfoWindow("Manual de CamaraWin", InfoDocuments.Manual()) { Owner = this }.Show();
    void License_Click(object sender, RoutedEventArgs e) => new InfoWindow("Licencia", InfoDocuments.License()) { Owner = this }.Show();
    void Support_Click(object sender, RoutedEventArgs e) => new InfoWindow("Soporte", InfoDocuments.Support()) { Owner = this }.Show();
    void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();
}
