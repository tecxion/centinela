using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform.Linux;

/// <summary>
/// An XDG autostart entry (<c>&lt;autostartDir&gt;/centinela.desktop</c>) that starts this program with
/// <c>--tray</c> at login. Plain file access, so it needs no platform guard.
/// </summary>
public sealed class LinuxAutoStart(string autostartDir, string exePath) : IAutoStart
{
    const string FileName = "centinela.desktop";
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    string EntryPath => Path.Combine(autostartDir, FileName);

    /// <summary>
    /// <c>$XDG_CONFIG_HOME/autostart</c> when that variable is an absolute path; otherwise <c>~/.config/autostart</c>.
    /// </summary>
    public static string DefaultDirectory(Func<string, string?> env, string home)
    {
        var config = env("XDG_CONFIG_HOME") is { } c && c.StartsWith('/') ? c.TrimEnd('/') : $"{home.TrimEnd('/')}/.config";
        return $"{config}/autostart";
    }

    /// <summary>The entry exists, runs this program and the desktop has not switched it off.</summary>
    public bool IsEnabled => Read() is { } keys && IsActive(keys) && Program(keys) == exePath;

    public void Enable()
    {
        Directory.CreateDirectory(autostartDir);
        var temp = Path.Combine(autostartDir, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, DesktopEntry.AutoStart(exePath), Utf8NoBom);
            File.Move(temp, EntryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Disable()
    {
        if (File.Exists(EntryPath)) File.Delete(EntryPath);
    }

    /// <summary>
    /// Rewrites an active entry that runs another Centinela (an older install elsewhere) so it runs this one.
    /// An entry the user switched off in the desktop settings is left alone.
    /// </summary>
    public bool Migrate()
    {
        if (Read() is not { } keys || !IsActive(keys) || Program(keys) == exePath) return false;
        Enable();
        return true;
    }

    Dictionary<string, string>? Read()
    {
        try
        {
            return File.Exists(EntryPath) ? DesktopEntry.ReadGroup(File.ReadAllText(EntryPath)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static bool IsActive(Dictionary<string, string> keys) =>
        !IsTrue(keys, "Hidden") && !(keys.TryGetValue("X-GNOME-Autostart-enabled", out var on) && on.Trim() == "false");

    static bool IsTrue(Dictionary<string, string> keys, string key) =>
        keys.TryGetValue(key, out var value) && value.Trim() == "true";

    static string? Program(Dictionary<string, string> keys) =>
        keys.TryGetValue("Exec", out var exec) ? DesktopEntry.ExecProgram(exec) : null;
}
