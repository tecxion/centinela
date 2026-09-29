using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centinela.Core;

public sealed class BackupFormatException(string message) : Exception(message);

public sealed class BackupPassphraseException() : Exception("La clave no es correcta o el archivo está dañado.");

public sealed record BackupImport(IReadOnlyList<Camera> Cameras, bool WasEncrypted);

public static class CameraBackup
{
    public const string Format = "centinela-cameras";
    /// <summary>Written by the app before it was renamed from CamaraWin; still imported.</summary>
    public const string LegacyFormat = "camarawin-cameras";
    public const int Version = 1;
    public const int Iterations = 600_000;
    const int SaltSize = 16, KeySize = 32, NonceSize = 12, TagSize = 16;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Export(IEnumerable<Camera> cameras, string? passphrase, DateTime? now = null)
    {
        byte[]? salt = null, key = null;
        if (!string.IsNullOrEmpty(passphrase))
        {
            salt = RandomNumberGenerator.GetBytes(SaltSize);
            key = DeriveKey(passphrase, salt);
        }
        var file = new BackupFile
        {
            Format = Format,
            Version = Version,
            ExportedAt = (now ?? DateTime.UtcNow).ToUniversalTime(),
            Encryption = salt is null ? null : new EncryptionInfo { Salt = Convert.ToBase64String(salt) },
            Cameras = cameras.OrderBy(c => c.Order).Select(c =>
            {
                var mainUrl = Strip(c.MainUrlOverride);
                var subUrl = Strip(c.SubUrlOverride);
                return new BackupCamera
                {
                    Name = c.Name,
                    Brand = c.Brand,
                    Host = c.Host,
                    Port = c.Port,
                    User = c.User,
                    Password = key is null || c.Password.Length == 0
                        ? null
                        : Encrypt(key, c.Password, Aad(c.Name, c.Host, c.Port, mainUrl, subUrl)),
                    MainUrl = mainUrl,
                    SubUrl = subUrl,
                    UseUdp = c.UseUdp,
                    MotionEnabled = c.MotionEnabled,
                    MotionSensitivity = c.MotionSensitivity,
                    MotionCooldownSeconds = c.MotionCooldownSeconds,
                    ConnectionAlerts = c.ConnectionAlerts,
                    MotionAlerts = c.MotionAlerts,
                    LowQualityWhenBig = c.LowQualityWhenBig,
                    Smoothing = c.Smoothing,
                };
            }).ToList(),
        };
        return JsonSerializer.Serialize(file, Json);
    }

    public static BackupImport Import(string json, Func<string?> askPassphrase)
    {
        BackupFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BackupFile>(json, Json);
        }
        catch (JsonException)
        {
            throw new BackupFormatException("El archivo está dañado o no es un JSON válido.");
        }
        if (file is null || (file.Format != Format && file.Format != LegacyFormat))
            throw new BackupFormatException("El archivo no es una copia de cámaras de Centinela.");
        if (file.Version > Version)
            throw new BackupFormatException("Esta copia se hizo con una versión más nueva de Centinela. Actualiza la aplicación.");
        if (file.Version != Version)
            throw new BackupFormatException("Versión de copia no soportada.");

        byte[]? key = null;
        if (file.Encryption is { } encryption)
        {
            if (encryption.Iterations != Iterations || encryption.Algorithm != "AES-256-GCM" || encryption.Kdf != "PBKDF2-SHA256")
                throw new BackupFormatException("Parámetros de cifrado no soportados.");
            var salt = DecodeSalt(encryption.Salt);
            var passphrase = askPassphrase() ?? throw new OperationCanceledException();
            key = DeriveKey(passphrase, salt);
        }

        var cameras = new List<Camera>();
        foreach (var (entry, index) in (file.Cameras ?? []).Select((e, i) => (e, i)))
        {
            if (entry is null)
                throw new BackupFormatException("El archivo está dañado o no es un JSON válido.");
            var name = string.IsNullOrWhiteSpace(entry.Name) ? $"Cámara {index + 1}" : entry.Name;
            var host = entry.Host ?? "";
            var port = entry.Port is > 0 and <= 65535 ? entry.Port : 554;
            cameras.Add(new Camera
            {
                Name = name,
                Brand = Enum.IsDefined(entry.Brand) ? entry.Brand : Brand.Custom,
                Host = host,
                Port = port,
                User = entry.User ?? "",
                Password = key is null || entry.Password is null ? "" : Decrypt(key, entry.Password, Aad(entry.Name ?? "", host, entry.Port, entry.MainUrl, entry.SubUrl)),
                MainUrlOverride = entry.MainUrl,
                SubUrlOverride = entry.SubUrl,
                UseUdp = entry.UseUdp,
                Order = index,
                MotionEnabled = entry.MotionEnabled,
                MotionSensitivity = Enum.IsDefined(entry.MotionSensitivity) ? entry.MotionSensitivity : MotionSensitivity.Medium,
                MotionCooldownSeconds = Camera.NormalizeCooldown(entry.MotionCooldownSeconds),
                ConnectionAlerts = entry.ConnectionAlerts,
                MotionAlerts = entry.MotionAlerts,
                LowQualityWhenBig = entry.LowQualityWhenBig,
                Smoothing = entry.Smoothing,
            });
        }
        return new BackupImport(cameras, key is not null);
    }

    /// <summary>
    /// Whether the file carries encrypted passwords (an <c>encryption</c> object at the root). Never throws:
    /// malformed files report false so that <see cref="Import"/> produces its own format error.
    /// </summary>
    public static bool IsEncrypted(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("encryption", out var encryption)
                && encryption.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static string? Strip(string? url) => url is null ? null : StreamUrlBuilder.StripCredentials(url, out _);

    static byte[] DecodeSalt(string? salt)
    {
        try
        {
            var bytes = Convert.FromBase64String(salt ?? "");
            if (bytes.Length == SaltSize) return bytes;
        }
        catch (FormatException)
        {
        }
        throw new BackupFormatException("Parámetros de cifrado no soportados.");
    }

    /// <summary>Binds each encrypted password to the entry's identity and override URLs.</summary>
    static string Aad(string name, string host, int port, string? mainUrl, string? subUrl) =>
        $"{name}|{host}|{port}|{mainUrl ?? ""}|{subUrl ?? ""}";

    static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    static string Encrypt(byte[] key, string plaintext, string aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(aad));
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    static string Decrypt(byte[] key, string payload, string aad)
    {
        try
        {
            var bytes = Convert.FromBase64String(payload);
            if (bytes.Length < NonceSize + TagSize) throw new BackupPassphraseException();
            var nonce = bytes.AsSpan(0, NonceSize);
            var tag = bytes.AsSpan(bytes.Length - TagSize);
            var cipher = bytes.AsSpan(NonceSize, bytes.Length - NonceSize - TagSize);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(aad));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            throw new BackupPassphraseException();
        }
    }

    sealed class BackupFile
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public DateTime ExportedAt { get; set; }
        public EncryptionInfo? Encryption { get; set; }
        public List<BackupCamera>? Cameras { get; set; }
    }

    sealed class EncryptionInfo
    {
        public string Algorithm { get; set; } = "AES-256-GCM";
        public string Kdf { get; set; } = "PBKDF2-SHA256";
        public int Iterations { get; set; } = CameraBackup.Iterations;
        public string Salt { get; set; } = "";
    }

    sealed class BackupCamera
    {
        public string? Name { get; set; }
        [JsonConverter(typeof(LenientBrandConverter))]
        public Brand Brand { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 554;
        public string? User { get; set; }
        public string? Password { get; set; }
        public string? MainUrl { get; set; }
        public string? SubUrl { get; set; }
        public bool UseUdp { get; set; }
        public bool MotionEnabled { get; set; }
        [JsonConverter(typeof(LenientSensitivityConverter))]
        public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;
        public int MotionCooldownSeconds { get; set; } = 60;
        public bool ConnectionAlerts { get; set; } = true;
        public bool MotionAlerts { get; set; } = true;
        public bool LowQualityWhenBig { get; set; }
        public bool Smoothing { get; set; }
    }
}
