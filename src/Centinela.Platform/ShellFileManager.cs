using System.Diagnostics;
using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>macOS and Linux: the desktop's default handler opens the folder (a file reveals its folder).</summary>
public sealed class ShellFileManager : IFileManager
{
    public void OpenFolder(string folder) => Start(CommandForFolder(folder));

    public void Reveal(string path)
    {
        if (Directory.Exists(path)) Start(CommandForFolder(path));
        else if (File.Exists(path) && Path.GetDirectoryName(path) is { } folder) Start(CommandForFolder(folder));
    }

    internal static ProcessStartInfo CommandForFolder(string folder) => new(folder) { UseShellExecute = true };

    static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (Exception) { /* nothing to show if the file manager cannot start */ }
    }
}
