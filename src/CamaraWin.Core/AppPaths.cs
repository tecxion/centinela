namespace CamaraWin.Core;

public static class AppPaths
{
    const string AppFolder = "CamaraWin";

    /// <summary>Overrides <see cref="DataDirectory"/> when set and non-empty (tests and smoke runs).</summary>
    public const string DataDirectoryVariable = "CAMARAWIN_DATA_DIR";

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

    public static string RecordingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), AppFolder);

    public static string SnapshotsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppFolder);

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string DefaultBackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppFolder);

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
