using System.Diagnostics;
using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>
/// macOS and Linux: <c>open</c> (macOS; a file is shown selected) or <c>xdg-open</c> (Linux; a file opens its
/// folder). Folders or files that do not exist are ignored.
/// </summary>
public sealed class ShellFileManager : IFileManager
{
    readonly OsKind _os;
    readonly Action<ProcessStartInfo> _start;

    public ShellFileManager() : this(OsInfo.Current, Start) { }

    internal ShellFileManager(OsKind os, Action<ProcessStartInfo> start)
    {
        _os = os;
        _start = start;
    }

    public void OpenFolder(string folder)
    {
        if (Directory.Exists(folder)) _start(CommandForFolder(_os, folder));
    }

    public void Reveal(string path)
    {
        if (Directory.Exists(path)) _start(CommandForFolder(_os, path));
        else if (File.Exists(path)) _start(CommandForFile(_os, path));
    }

    internal static ProcessStartInfo CommandForFolder(OsKind os, string folder) =>
        new(os == OsKind.MacOS ? "open" : "xdg-open") { ArgumentList = { folder } };

    internal static ProcessStartInfo CommandForFile(OsKind os, string file) => os == OsKind.MacOS
        ? new ProcessStartInfo("open") { ArgumentList = { "-R", file } }
        // xdg-open has no way to select a file: open the folder that holds it. Split on '/' (not
        // Path.GetDirectoryName) so the command is the same wherever it is built.
        : CommandForFolder(os, file.LastIndexOf('/') is > 0 and var cut ? file[..cut] : "/");

    static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (Exception) { /* nothing to show if the file manager cannot start */ }
    }
}
