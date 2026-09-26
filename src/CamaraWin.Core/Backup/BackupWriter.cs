using System.Text;

namespace CamaraWin.Core;

public static class BackupWriter
{
    public const string AutomaticFileName = "camaras-copia.json";

    /// <summary>Writes the password-less automatic copy atomically and returns its path.</summary>
    public static string WriteAutomatic(string folder, IEnumerable<Camera> cameras)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, AutomaticFileName);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(CameraBackup.Export(cameras, passphrase: null));
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return path;
    }
}
