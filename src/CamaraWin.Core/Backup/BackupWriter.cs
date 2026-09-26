namespace CamaraWin.Core;

public static class BackupWriter
{
    public const string AutomaticFileName = "camaras-copia.json";

    /// <summary>Writes the password-less automatic copy atomically and returns its path.</summary>
    public static string WriteAutomatic(string folder, IEnumerable<Camera> cameras)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, AutomaticFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, CameraBackup.Export(cameras, passphrase: null));
        File.Move(tmp, path, overwrite: true);
        return path;
    }
}
