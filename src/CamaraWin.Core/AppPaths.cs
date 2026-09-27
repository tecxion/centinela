using System.Security.Cryptography;
using System.Text;

namespace CamaraWin.Core;

public static class AppPaths
{
    const string AppFolder = "CamaraWin";

    /// <summary>
    /// When set and non-empty, every folder the app writes to lives under it and the running instance is
    /// separate from the user's normal one (tests and smoke runs).
    /// </summary>
    public const string DataDirectoryVariable = "CAMARAWIN_DATA_DIR";

    static string? Override =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden ? overridden : null;

    public static bool IsDataDirectoryOverridden => Override is not null;

    public static string DataDirectory =>
        Override ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

    public static string RecordingsDirectory => UserFolder(Environment.SpecialFolder.MyVideos, "recordings");

    public static string SnapshotsDirectory => UserFolder(Environment.SpecialFolder.MyPictures, "snapshots");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string DefaultBackupDirectory => UserFolder(Environment.SpecialFolder.MyDocuments, "backup");

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

    static string UserFolder(Environment.SpecialFolder folder, string overrideName) =>
        Override is { } root ? Path.Combine(root, overrideName) : Path.Combine(Environment.GetFolderPath(folder), AppFolder);

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
