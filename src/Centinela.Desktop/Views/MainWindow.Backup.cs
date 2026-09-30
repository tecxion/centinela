using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Centinela.Desktop.Views;

/// <summary>«Carpeta de copia automática…» in the Archivo menu.</summary>
public partial class MainWindow
{
    async void BackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        // A folder that does not exist yet (or a malformed setting) gives null: the picker then opens where it likes.
        IStorageFolder? start;
        try
        {
            start = await StorageProvider.TryGetFolderFromPathAsync(_vm.BackupFolder);
        }
        catch (Exception)
        {
            start = null;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Carpeta de copia automática",
            SuggestedStartLocation = start,
        });
        if (_closing || folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        _vm.SetBackupFolder(path);
    }
}
