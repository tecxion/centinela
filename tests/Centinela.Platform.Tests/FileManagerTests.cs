using Centinela.Platform;
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class FileManagerTests
{
    [Fact]
    public void Explorer_opens_folders_and_selects_files()
    {
        var folder = ExplorerFileManager.CommandForFolder(@"C:\Vídeos\Centinela");
        Assert.Equal(("explorer.exe", "\"C:\\Vídeos\\Centinela\""), (folder.FileName, folder.Arguments));
        var file = ExplorerFileManager.CommandForFile(@"C:\Imágenes\Centinela\a.png");
        Assert.Equal(("explorer.exe", "/select,\"C:\\Imágenes\\Centinela\\a.png\""), (file.FileName, file.Arguments));
    }

    [Fact]
    public void Explorer_never_ends_a_quoted_folder_with_a_backslash()
    {
        // "C:\" would reach explorer as C:" (the backslash escapes the quote).
        Assert.Equal(@"D:\", ExplorerFileManager.CommandForFolder(@"D:\").Arguments);
        Assert.Equal("\"C:\\Vídeos\\Centinela\"", ExplorerFileManager.CommandForFolder(@"C:\Vídeos\Centinela\").Arguments);
    }

    [Fact]
    public void Shell_opens_the_folder_with_the_desktop_default()
    {
        var info = ShellFileManager.CommandForFolder("/home/ana/Videos/Centinela");
        Assert.Equal("/home/ana/Videos/Centinela", info.FileName);
        Assert.True(info.UseShellExecute);
    }
}
