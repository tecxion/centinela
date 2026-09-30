namespace Centinela.Core.Platform;

/// <summary>Shows folders and files in the system's file manager.</summary>
public interface IFileManager
{
    void OpenFolder(string folder);

    /// <summary>A folder is opened; a file is shown selected where the system can, otherwise its folder is opened.</summary>
    void Reveal(string path);
}
