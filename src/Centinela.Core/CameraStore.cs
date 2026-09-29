using System.Text.Json;
using System.Text.Json.Serialization;
using Centinela.Core.Platform;

namespace Centinela.Core;

public sealed class CameraStore(string filePath, ISecretStore secrets)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "cameras.json");

    public IReadOnlyList<Camera> Load()
    {
        if (!File.Exists(filePath)) return [];
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked or unreadable right now: start empty and leave the file exactly as it is.
            return [];
        }

        List<CameraDto> dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<CameraDto>>(text, Json) ?? [];
        }
        catch (JsonException)
        {
            // Keep the unreadable file so a later Save never silently overwrites the user's data.
            try
            {
                File.Move(filePath, $"{filePath}.bad-{DateTime.Now:yyyyMMddHHmmss}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Could not set it aside: leave it untouched.
            }
            return [];
        }
        return dtos.Select(FromDto).OrderBy(c => c.Order).ToList();
    }

    public void Save(IEnumerable<Camera> cameras)
    {
        var list = cameras.ToList();
        var previous = ReadStoredSecrets();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list.Select(ToDto).ToList(), Json));
        File.Move(tmp, filePath, overwrite: true);

        // A camera that is no longer in the list takes its password with it (keychain entries, etc.).
        var kept = list.Select(c => c.Id).ToHashSet();
        foreach (var (id, stored) in previous)
            if (!kept.Contains(id) && stored.Length > 0) secrets.Forget(id, stored);
    }

    /// <summary>Id → stored password value of the file as it is now; empty when missing or unreadable.</summary>
    Dictionary<Guid, string> ReadStoredSecrets()
    {
        try
        {
            if (!File.Exists(filePath)) return [];
            var dtos = JsonSerializer.Deserialize<List<CameraDto>>(File.ReadAllText(filePath), Json) ?? [];
            return dtos.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().PasswordProtected ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Credentials typed inside an override URL never reach the file: they are stripped from the URL
    /// and, when the camera has no user of its own, kept in User / PasswordProtected instead
    /// (<see cref="StreamUrlBuilder.Build"/> puts them back into the URL).
    /// </summary>
    CameraDto ToDto(Camera c)
    {
        var main = StripCredentials(c.MainUrlOverride, out var mainCredentials);
        var sub = StripCredentials(c.SubUrlOverride, out var subCredentials);
        var (user, password) = c.User.Length == 0 && (mainCredentials ?? subCredentials) is { } fromUrl
            ? (fromUrl.User, fromUrl.Password)
            : (c.User, c.Password);
        return new()
        {
            Id = c.Id, Name = c.Name, Brand = c.Brand, Host = c.Host, Port = c.Port, User = user,
            PasswordProtected = secrets.Protect(c.Id, password), MainUrlOverride = main,
            SubUrlOverride = sub, UseUdp = c.UseUdp, Order = c.Order,
            MotionEnabled = c.MotionEnabled, MotionSensitivity = c.MotionSensitivity,
            MotionCooldownSeconds = c.MotionCooldownSeconds, ConnectionAlerts = c.ConnectionAlerts, MotionAlerts = c.MotionAlerts,
            LowQualityWhenBig = c.LowQualityWhenBig, Smoothing = c.Smoothing, OriginalEncoders = c.OriginalEncoders,
        };
    }

    static string? StripCredentials(string? url, out UrlCredentials? credentials)
    {
        credentials = null;
        return url is null ? null : StreamUrlBuilder.StripCredentials(url.Trim(), out credentials);
    }

    Camera FromDto(CameraDto d) => new()
    {
        Id = d.Id, Name = d.Name, Brand = Enum.IsDefined(d.Brand) ? d.Brand : Brand.Custom, Host = d.Host, Port = d.Port, User = d.User,
        Password = secrets.Unprotect(d.Id, d.PasswordProtected ?? "") ?? "", MainUrlOverride = d.MainUrlOverride,
        SubUrlOverride = d.SubUrlOverride, UseUdp = d.UseUdp, Order = d.Order,
        MotionEnabled = d.MotionEnabled,
        MotionSensitivity = Enum.IsDefined(d.MotionSensitivity) ? d.MotionSensitivity : MotionSensitivity.Medium,
        MotionCooldownSeconds = Camera.NormalizeCooldown(d.MotionCooldownSeconds),
        ConnectionAlerts = d.ConnectionAlerts, MotionAlerts = d.MotionAlerts,
        LowQualityWhenBig = d.LowQualityWhenBig, Smoothing = d.Smoothing, OriginalEncoders = d.OriginalEncoders,
    };

    sealed class CameraDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        [JsonConverter(typeof(LenientBrandConverter))]
        public Brand Brand { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 554;
        public string User { get; set; } = "";
        public string? PasswordProtected { get; set; }
        public string? MainUrlOverride { get; set; }
        public string? SubUrlOverride { get; set; }
        public bool UseUdp { get; set; }
        public int Order { get; set; }
        public bool MotionEnabled { get; set; }
        [JsonConverter(typeof(LenientSensitivityConverter))]
        public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;
        public int MotionCooldownSeconds { get; set; } = 60;
        public bool ConnectionAlerts { get; set; } = true;
        public bool MotionAlerts { get; set; } = true;
        public bool LowQualityWhenBig { get; set; }
        public bool Smoothing { get; set; }
        public string? OriginalEncoders { get; set; }
    }
}
