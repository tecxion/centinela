using Centinela.Core;
using Centinela.Desktop.Services.Backup;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.ViewModels;

/// <summary>The automatic copy after every save of the camera list, and its folder.</summary>
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
