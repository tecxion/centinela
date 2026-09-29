using System.Security.Cryptography;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Core;

public static class AppPaths
{
    const string AppFolder = "Centinela";

    /// <summary>
    /// When set and non-empty, every folder the app writes to lives under it and the running instance is
    /// separate from the user's normal one (tests and smoke runs).
    /// </summary>
    public const string DataDirectoryVariable = "CENTINELA_DATA_DIR";

    static string? Override =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden ? overridden : null;

    public static bool IsDataDirectoryOverridden => Override is not null;

    /// <summary>This system's folders (Windows: AppData, Vídeos…; macOS: Application Support, Películas…; Linux: XDG).</summary>
    public static UserFolders Folders => PlatformFolders.Current;

    public static string DataDirectory =>
        Override ?? Folders.Data;

    public static string RecordingsDirectory => UserFolder(Folders.Videos, "recordings");

    public static string SnapshotsDirectory => UserFolder(Folders.Pictures, "snapshots");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string DefaultBackupDirectory => UserFolder(Folders.Documents, "backup");

    /// <summary>
    /// Empty normally; with the override, "." plus 8 hex digits of the override folder's hash, so a test
    /// run never talks to (or activates) the user's instance. The same folder always yields the same suffix.
    /// </summary>
    public static string InstanceSuffix
    {
        get
        {
            if (Override is not { } root) return "";
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "." + Convert.ToHexStringLower(hash.AsSpan(0, 4));
        }
    }

    static string UserFolder(string folder, string overrideName) =>
        Override is { } root ? Path.Combine(root, overrideName) : Path.Combine(folder, AppFolder);

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "camara" : cleaned;
    }

    public static string RecordingFile(string cameraName, DateTime time) =>
        Path.Combine(RecordingsDirectory, SanitizeFileName(cameraName), $"{time:yyyy-MM-dd_HH-mm-ss}.mkv");

    public static string SnapshotFile(string cameraName, DateTime time) =>
        Path.Combine(SnapshotsDirectory, $"{SanitizeFileName(cameraName)}_{time:yyyy-MM-dd_HH-mm-ss}.png");
}
