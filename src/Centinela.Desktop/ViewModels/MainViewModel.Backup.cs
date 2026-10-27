using Centinela.Core;
using Centinela.Desktop.Services.Backup;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.ViewModels;

/// <summary>The automatic copy after every save of the camera list, its folder, and applying an import.</summary>
sealed partial class MainViewModel
{
    public AutomaticBackup Backup { get; }

    /// <summary>The chosen folder, or Documents\Centinela\backup when none is set.</summary>
    public string BackupFolder =>
        string.IsNullOrWhiteSpace(Settings.Settings.BackupFolder) ? AppPaths.DefaultBackupDirectory : Settings.Settings.BackupFolder;

    void InitBackup()
    {
        Cameras.Saved += ScheduleBackup;
        Backup.Failed += message => UiThread.Post(() => Notify(message, null));
    }

    /// <summary>«Carpeta de copia automática…»: keeps the folder and writes a copy there.</summary>
    public void SetBackupFolder(string folder)
    {
        Settings.Settings.BackupFolder = folder;
        Settings.Save();
        ScheduleBackup();
    }

    /// <summary>
    /// Merges an imported list into the cameras: updated ones stop recording (with a notice) and have their tiles
    /// retired, since they may point at new URLs or credentials; then saves (which schedules the automatic copy)
    /// and rebuilds the view. Returns the summary to show.
    /// </summary>
    public string ApplyImport(BackupImport imported, Action<Guid> retireTiles)
    {
        var merge = BackupMerge.Merge(Cameras.All, imported.Cameras);
        foreach (var id in merge.UpdatedIds)
        {
            if (Recordings.IsRecording(id)) Recordings.StopWithNotice(id);
            retireTiles(id);
        }
        Cameras.ReplaceAll(merge.Cameras);
        Cameras.Save();
        CamerasChanged();
        return $"{merge.Added} añadidas, {merge.Updated} actualizadas, {merge.WithoutPassword} sin contraseña.";
    }

    // Saved is raised outside the repository's try, so this must not throw.
    void ScheduleBackup()
    {
        try
        {
            Backup.Schedule(BackupFolder, Cameras.All);
        }
        catch (Exception ex)
        {
            Notify($"No se pudo guardar la copia automática: {ex.Message}", null);
        }
    }
}
