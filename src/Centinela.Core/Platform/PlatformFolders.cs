namespace Centinela.Core.Platform;

/// <param name="Data">Centinela's own data folder (cameras, settings, logs).</param>
/// <param name="Videos">The user's videos folder (recordings go in a «Centinela» folder inside).</param>
public sealed record UserFolders(string Data, string Videos, string Pictures, string Documents);

/// <summary>Where each system keeps app data and the user's videos, pictures and documents.</summary>
public static class PlatformFolders
{
    static readonly Lazy<UserFolders> Real = new(() => For(OsInfo.Current,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable,
        Environment.GetFolderPath, ReadUserDirs()));

    /// <summary>This process's folders (computed once).</summary>
    public static UserFolders Current => Real.Value;

    /// <param name="userDirs">Contents of Linux's <c>user-dirs.dirs</c>, or null.</param>
    public static UserFolders For(OsKind os, string home, Func<string, string?> env,
        Func<Environment.SpecialFolder, string> special, string? userDirs) => os switch
    {
        OsKind.Windows => new UserFolders(Path.Combine(special(Environment.SpecialFolder.ApplicationData), "Centinela"),
            special(Environment.SpecialFolder.MyVideos), special(Environment.SpecialFolder.MyPictures),
            special(Environment.SpecialFolder.MyDocuments)),
        OsKind.MacOS => new UserFolders($"{home}/Library/Application Support/Centinela",
            $"{home}/Movies", $"{home}/Pictures", $"{home}/Documents"),
        _ => Linux(home, env, userDirs),
    };

    static UserFolders Linux(string home, Func<string, string?> env, string? userDirs)
    {
        // The XDG spec says relative paths are invalid and must be ignored.
        var dataHome = (env("XDG_DATA_HOME") is { } d && d.StartsWith('/') ? d : $"{home}/.local/share").TrimEnd('/');
        var dirs = ParseUserDirs(userDirs, home);
        string Dir(string key, string fallback) => dirs.TryGetValue(key, out var value) ? value : $"{home}/{fallback}";
        return new UserFolders($"{dataHome}/centinela",
            Dir("XDG_VIDEOS_DIR", "Videos"), Dir("XDG_PICTURES_DIR", "Pictures"), Dir("XDG_DOCUMENTS_DIR", "Documents"));
    }

    /// <summary>
    /// Lines like <c>XDG_VIDEOS_DIR="$HOME/Vídeos"</c>; entries equal to $HOME itself and relative ones
    /// (neither $HOME-based nor absolute, which xdg-user-dirs does not allow) are skipped.
    /// </summary>
    static Dictionary<string, string> ParseUserDirs(string? text, string home)
    {
        var result = new Dictionary<string, string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var value = line[(equals + 1)..].Trim().Trim('"').Replace("$HOME", home, StringComparison.Ordinal).TrimEnd('/');
            if (!value.StartsWith('/') || value == home.TrimEnd('/')) continue;
            result[line[..equals].Trim()] = value;
        }
        return result;
    }

    static string? ReadUserDirs()
    {
        if (OsInfo.Current != OsKind.Linux) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { } c && c.StartsWith('/') ? c : $"{home}/.config";
        try
        {
            var file = Path.Combine(config, "user-dirs.dirs");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
