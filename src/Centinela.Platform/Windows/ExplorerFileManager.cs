using System.Diagnostics;
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

public sealed class ExplorerFileManager : IFileManager
{
    public void OpenFolder(string folder) => Start(CommandForFolder(folder));

    public void Reveal(string path)
    {
        if (Directory.Exists(path)) Start(CommandForFolder(path));
        else if (File.Exists(path)) Start(CommandForFile(path));
    }

    internal static ProcessStartInfo CommandForFolder(string folder) => new("explorer.exe", Quote(folder));

    internal static ProcessStartInfo CommandForFile(string file) => new("explorer.exe", $"/select,{Quote(file)}");

    /// <summary>
    /// Quoted path without a trailing backslash (it would escape the closing quote). A drive root keeps its
    /// backslash and goes unquoted: it has no spaces.
    /// </summary>
    static string Quote(string path)
    {
        var trimmed = path.TrimEnd('\\');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : $"\"{trimmed}\"";
    }

    static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (Exception) { /* nothing to show if the file manager cannot start */ }
    }
}
