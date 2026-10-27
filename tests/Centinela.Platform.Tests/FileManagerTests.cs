using Centinela.Core.Platform;
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
    public void Linux_opens_folders_and_files_with_xdg_open()
    {
        var folder = ShellFileManager.CommandForFolder(OsKind.Linux, "/home/ana/Vídeos/Centinela");
        Assert.Equal("xdg-open", folder.FileName);
        Assert.Equal(["/home/ana/Vídeos/Centinela"], folder.ArgumentList);
        Assert.False(folder.UseShellExecute);
        // xdg-open cannot select a file: it opens the file's folder.
        var file = ShellFileManager.CommandForFile(OsKind.Linux, "/home/ana/Imágenes/Centinela/a b.png");
        Assert.Equal("xdg-open", file.FileName);
        Assert.Equal(["/home/ana/Imágenes/Centinela"], file.ArgumentList);
    }

    [Fact]
    public void MacOS_opens_folders_and_reveals_files_with_open()
    {
        var folder = ShellFileManager.CommandForFolder(OsKind.MacOS, "/Users/ana/Movies/Centinela");
        Assert.Equal("open", folder.FileName);
        Assert.Equal(["/Users/ana/Movies/Centinela"], folder.ArgumentList);
        var file = ShellFileManager.CommandForFile(OsKind.MacOS, "/Users/ana/Pictures/Centinela/a b.png");
        Assert.Equal("open", file.FileName);
        Assert.Equal(["-R", "/Users/ana/Pictures/Centinela/a b.png"], file.ArgumentList);
    }

    [Fact]
    public void Shell_does_nothing_for_a_folder_that_does_not_exist()
    {
        var started = new List<System.Diagnostics.ProcessStartInfo>();
        var files = new ShellFileManager(OsKind.Linux, started.Add);
        var missing = Path.Combine(Path.GetTempPath(), "centinela-no-existe-" + Guid.NewGuid());
        files.OpenFolder(missing);
        files.Reveal(missing);
        Assert.Empty(started);
    }

    [Fact]
    public void Shell_starts_one_command_per_request()
    {
        var dir = Directory.CreateTempSubdirectory("centinela-fm-").FullName;
        try
        {
            var file = Path.Combine(dir, "a.png");
            File.WriteAllBytes(file, [1]);
            var started = new List<System.Diagnostics.ProcessStartInfo>();
            var files = new ShellFileManager(OsKind.MacOS, started.Add);
            files.OpenFolder(dir);
            files.Reveal(dir);
            files.Reveal(file);
            Assert.Equal([["{dir}"], ["{dir}"], ["-R", "{file}"]],
                started.Select(i => i.ArgumentList.Select(a => a.Replace(file, "{file}").Replace(dir, "{dir}")).ToArray()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
