using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centinela.Core;

public sealed class CameraStore(string filePath)
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
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cameras.Select(ToDto).ToList(), Json));
        File.Move(tmp, filePath, overwrite: true);
    }

    /// <summary>
    /// Credentials typed inside an override URL never reach the file: they are stripped from the URL
    /// and, when the camera has no user of its own, kept in User / PasswordProtected instead
    /// (<see cref="StreamUrlBuilder.Build"/> puts them back into the URL).
    /// </summary>
    static CameraDto ToDto(Camera c)
    {
        var main = StripCredentials(c.MainUrlOverride, out var mainCredentials);
        var sub = StripCredentials(c.SubUrlOverride, out var subCredentials);
        var (user, password) = c.User.Length == 0 && (mainCredentials ?? subCredentials) is { } fromUrl
            ? (fromUrl.User, fromUrl.Password)
            : (c.User, c.Password);
        return new()
        {
            Id = c.Id, Name = c.Name, Brand = c.Brand, Host = c.Host, Port = c.Port, User = user,
            PasswordProtected = Protect(password), MainUrlOverride = main,
            SubUrlOverride = sub, UseUdp = c.UseUdp, Order = c.Order,
            MotionEnabled = c.MotionEnabled, MotionSensitivity = c.MotionSensitivity,
            MotionCooldownSeconds = c.MotionCooldownSeconds, ConnectionAlerts = c.ConnectionAlerts, MotionAlerts = c.MotionAlerts,
        };
    }

    static string? StripCredentials(string? url, out UrlCredentials? credentials)
    {
        credentials = null;
        return url is null ? null : StreamUrlBuilder.StripCredentials(url.Trim(), out credentials);
    }

    static Camera FromDto(CameraDto d) => new()
    {
        Id = d.Id, Name = d.Name, Brand = Enum.IsDefined(d.Brand) ? d.Brand : Brand.Custom, Host = d.Host, Port = d.Port, User = d.User,
        Password = Unprotect(d.PasswordProtected), MainUrlOverride = d.MainUrlOverride,
        SubUrlOverride = d.SubUrlOverride, UseUdp = d.UseUdp, Order = d.Order,
        MotionEnabled = d.MotionEnabled,
        MotionSensitivity = Enum.IsDefined(d.MotionSensitivity) ? d.MotionSensitivity : MotionSensitivity.Medium,
        MotionCooldownSeconds = Camera.NormalizeCooldown(d.MotionCooldownSeconds),
        ConnectionAlerts = d.ConnectionAlerts, MotionAlerts = d.MotionAlerts,
    };

    static string Protect(string plain) => plain.Length == 0
        ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return "";
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Undecryptable (e.g. file from another Windows user): the camera just loses its password.
            return "";
        }
    }

    sealed class CameraDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
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
        public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;
        public int MotionCooldownSeconds { get; set; } = 60;
        public bool ConnectionAlerts { get; set; } = true;
        public bool MotionAlerts { get; set; } = true;
    }
}
