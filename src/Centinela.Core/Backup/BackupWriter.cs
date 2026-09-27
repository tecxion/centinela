using System.Text;

namespace Centinela.Core;

public static class BackupWriter
{
    public const string AutomaticFileName = "camaras-copia.json";

    /// <summary>Writes the password-less automatic copy atomically and returns its path.</summary>
    public static string WriteAutomatic(string folder, IEnumerable<Camera> cameras)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, AutomaticFileName);
        WriteAtomic(path, CameraBackup.Export(cameras, passphrase: null));
        return path;
    }

    /// <summary>
    /// Writes UTF-8 text (no BOM) to a temporary file next to <paramref name="path"/>, flushes it to disk and
    /// moves it over the target, so a crash never leaves a half-written file. On failure the target is untouched.
    /// </summary>
    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
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
    }
}
