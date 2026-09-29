using System.Security.Cryptography;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>
/// AES-256-GCM with a random key kept in <paramref name="keyFile"/> (owner-only on Unix). For systems without a
/// keychain and for test runs. Each value is bound to its camera (the camera Id is the associated data).
/// Stored form: "file:" + base64(nonce[12] | tag[16] | ciphertext).
/// </summary>
public sealed class FileSecretStore(string keyFile) : ISecretStore
{
    const int NonceSize = 12;
    const int TagSize = 16;
    byte[]? _key;

    public string Protect(Guid cameraId, string password)
    {
        if (password.Length == 0) return "";
        var plain = Encoding.UTF8.GetBytes(password);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Key(), TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, cameraId.ToByteArray());
        return SecretPrefix.File + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        var (prefix, payload) = SecretPrefix.Split(storedValue);
        if (prefix != SecretPrefix.File) return null;
        try
        {
            var data = Convert.FromBase64String(payload);
            if (data.Length < NonceSize + TagSize) return null;
            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(Key(), TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize),
                plain, cameraId.ToByteArray());
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null;
        }
    }

    public void Forget(Guid cameraId, string storedValue) { }

    byte[] Key()
    {
        if (_key is not null) return _key;
        if (File.Exists(keyFile))
        {
            var existing = File.ReadAllBytes(keyFile);
            if (existing.Length == 32) return _key = existing;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, key);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return _key = key;
    }
}
