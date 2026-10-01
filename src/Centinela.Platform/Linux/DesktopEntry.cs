using System.Text;

namespace Centinela.Platform.Linux;

/// <summary>
/// Writes and reads the freedesktop <c>.desktop</c> file that starts Centinela at login. Pure text: no file access.
/// </summary>
/// <remarks>
/// An <c>Exec</c> argument is escaped twice (Desktop Entry spec, "The Exec key"): first the shell-like quoting
/// (double quotes, with <c>"</c>, <c>`</c>, <c>$</c> and <c>\</c> preceded by a backslash), then the string-value
/// escaping of the whole file (<c>\\</c>, <c>\n</c>, <c>\t</c>, <c>\r</c>). So a backslash inside a quoted path
/// ends up as four backslashes in the file. A literal <c>%</c> is always written as <c>%%</c> (field codes).
/// </remarks>
public static class DesktopEntry
{
    public const string Group = "Desktop Entry";
    static readonly char[] Reserved = " \t\n\r\"'\\><~|&;$*?#()`".ToCharArray();

    /// <summary>The autostart entry that runs <paramref name="exePath"/> with <c>--tray</c>.</summary>
    public static string AutoStart(string exePath) =>
        $"[{Group}]\n" +
        "Type=Application\n" +
        "Name=Centinela\n" +
        "Comment=Visor de cámaras IP\n" +
        $"Exec={Quote(exePath)} --tray\n" +
        "Icon=centinela\n" +
        "Terminal=false\n" +
        "X-GNOME-Autostart-enabled=true\n";

    /// <summary>
    /// <paramref name="path"/> as the first <c>Exec</c> argument, exactly as written in the file: unchanged when it
    /// has no reserved character, otherwise in double quotes with the spec's escapes.
    /// </summary>
    public static string Quote(string path)
    {
        var arg = path.IndexOfAny(Reserved) < 0 ? path : QuoteArgument(path);
        return EscapeValue(arg.Replace("%", "%%"));
    }

    static string QuoteArgument(string path)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in path)
        {
            if (c is '"' or '`' or '$' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static string EscapeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r");

    /// <summary>The keys of the <c>[Desktop Entry]</c> group, with string escapes undone (the first value wins).</summary>
    public static Dictionary<string, string> ReadGroup(string text)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var inGroup = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inGroup = line.TrimEnd() == $"[{Group}]";
                continue;
            }
            if (!inGroup || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            keys.TryAdd(key, UnescapeValue(line[(eq + 1)..].TrimStart(' ', '\t')));
        }
        return keys;
    }

    static string UnescapeValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is 's' or 'n' or 't' or 'r' or '\\')
                sb.Append(value[++i] switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', _ => '\\' });
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    /// <summary>The program (first argument) of an already unescaped <c>Exec</c> value, or null when it is malformed.</summary>
    public static string? ExecProgram(string exec)
    {
        exec = exec.TrimStart(' ', '\t');
        if (exec.Length == 0) return null;
        var sb = new StringBuilder();
        if (exec[0] == '"')
        {
            for (var i = 1; i < exec.Length; i++)
            {
                var c = exec[i];
                if (c == '"') return sb.ToString().Replace("%%", "%");
                if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '`' or '$' or '\\') c = exec[++i];
                sb.Append(c);
            }
            return null; // Unterminated quote.
        }
        foreach (var c in exec)
        {
            if (c is ' ' or '\t' or '\n') break;
            sb.Append(c);
        }
        return sb.ToString().Replace("%%", "%");
    }
}
