using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamaraWin.Core;

public enum GridMode { Auto = 0, One = 1, Four = 4, Nine = 9, Sixteen = 16 }

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public bool Maximized { get; set; }
    public GridMode GridMode { get; set; } = GridMode.Auto;
}

public sealed class SettingsStore(string filePath)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        if (!Enum.IsDefined(settings.GridMode)) settings.GridMode = GridMode.Auto;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, Json));
    }
}
