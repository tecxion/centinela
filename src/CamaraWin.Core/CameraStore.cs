using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamaraWin.Core;

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
        var dtos = JsonSerializer.Deserialize<List<CameraDto>>(File.ReadAllText(filePath), Json) ?? [];
        return dtos.Select(FromDto).OrderBy(c => c.Order).ToList();
    }

    public void Save(IEnumerable<Camera> cameras)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cameras.Select(ToDto).ToList(), Json));
        File.Move(tmp, filePath, overwrite: true);
    }

    static CameraDto ToDto(Camera c) => new()
    {
        Id = c.Id, Name = c.Name, Brand = c.Brand, Host = c.Host, Port = c.Port, User = c.User,
        PasswordProtected = Protect(c.Password), MainUrlOverride = c.MainUrlOverride,
        SubUrlOverride = c.SubUrlOverride, UseUdp = c.UseUdp, Order = c.Order,
    };

    static Camera FromDto(CameraDto d) => new()
    {
        Id = d.Id, Name = d.Name, Brand = d.Brand, Host = d.Host, Port = d.Port, User = d.User,
        Password = Unprotect(d.PasswordProtected), MainUrlOverride = d.MainUrlOverride,
        SubUrlOverride = d.SubUrlOverride, UseUdp = d.UseUdp, Order = d.Order,
    };

    static string Protect(string plain) => plain.Length == 0
        ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    static string Unprotect(string? protectedValue) => string.IsNullOrEmpty(protectedValue)
        ? ""
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));

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
    }
}
