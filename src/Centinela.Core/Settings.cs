using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centinela.Core;

public enum GridMode { Auto = 0, One = 1, Four = 4, Nine = 9, Sixteen = 16 }

/// <summary>Stored by name in settings.json; Featured keeps its v1.1 meaning (thumbnails on the right).</summary>
public enum LayoutMode { Grid = 0, Featured = 1, FeaturedLeft = 2, Dual = 3 }

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public bool Maximized { get; set; }
    public GridMode GridMode { get; set; } = GridMode.Auto;
    public LayoutMode LayoutMode { get; set; } = LayoutMode.Grid;
    public Guid? FeaturedCameraId { get; set; }
    public bool ShowStats { get; set; }
    public string? BackupFolder { get; set; }
    public bool TrayHintShown { get; set; }
    /// <summary>The two big cameras of the Dual layout, left then right (may be stale; the planner validates).</summary>
    public List<Guid> DualCameraIds { get; set; } = [];
    /// <summary>Which big camera (0 left, 1 right) a click on a thumbnail replaces next.</summary>
    public int DualNextReplace { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }
    public bool SoundOnConnectionLost { get; set; }
    public bool SoundOnMotion { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string QuietFrom { get; set; } = "23:00";
    public string QuietTo { get; set; } = "07:00";
    /// <summary>Extra networks (a.b.c.0/24) ticked in «Buscar en red», besides this PC's and the cameras' own.</summary>
    public List<string> DiscoveryNetworks { get; set; } = [];

    /// <summary>Exactly "HH:mm" (00:00–23:59).</summary>
    public static bool TryParseTime(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
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
        if (!Enum.IsDefined(settings.LayoutMode)) settings.LayoutMode = LayoutMode.Grid;
        settings.DualCameraIds ??= [];
        settings.DiscoveryNetworks ??= [];
        if (settings.DualNextReplace is not (0 or 1)) settings.DualNextReplace = 0;
        if (!AppSettings.TryParseTime(settings.QuietFrom, out _)) settings.QuietFrom = "23:00";
        if (!AppSettings.TryParseTime(settings.QuietTo, out _)) settings.QuietTo = "07:00";
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, Json));
    }
}
