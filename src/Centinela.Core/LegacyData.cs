namespace Centinela.Core;

/// <summary>Carries the cameras and settings over from the app's former name (CamaraWin).</summary>
public static class LegacyData
{
    static readonly string[] Files = ["cameras.json", "settings.json"];

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CamaraWin");

    /// <summary>
    /// The first time only (no cameras.json in <paramref name="dataDirectory"/> yet), copies cameras.json and
    /// settings.json from <paramref name="legacyDirectory"/>. The old folder is left untouched. Returns whether it copied.
    /// </summary>
    public static bool Migrate(string legacyDirectory, string dataDirectory)
    {
        if (File.Exists(Path.Combine(dataDirectory, Files[0])) || !File.Exists(Path.Combine(legacyDirectory, Files[0])))
            return false;
        System.IO.Directory.CreateDirectory(dataDirectory);
        foreach (var name in Files)
        {
            var source = Path.Combine(legacyDirectory, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(dataDirectory, name), overwrite: true);
        }
        return true;
    }
}
