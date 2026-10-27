using System.Security.Cryptography;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>
/// AES-256-GCM with a random key kept in <paramref name="keyFile"/> (owner-only on Unix, created once and never overwritten). For systems without a
/// keychain and for test runs. Each value is bound to its camera (the camera Id is the associated data).
/// Stored form: "file:" + base64(nonce[12] | tag[16] | ciphertext).
/// </summary>
public sealed class FileSecretStore(string keyFile) : ISecretStore
{
    const int NonceSize = 12;
    const int TagSize = 16;
    const int KeySize = 32;
    const string DamagedMessage = "El archivo de claves de Centinela está dañado.";
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
            if (data.Length < NonceSize + TagSize || KeyIfAny() is not { } key) return null;
            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
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

    /// <summary>Protect: the key, created on first use. Never overwrites an existing key file.</summary>
    byte[] Key()
    {
        if (_key is not null) return _key;
        if (ReadKey() is { } existing) return _key = existing;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        var key = RandomNumberGenerator.GetBytes(KeySize);
        FileStream stream;
        try
        {
            stream = new FileStream(keyFile, CreateOptions());
        }
        catch (IOException) when (File.Exists(keyFile))
        {
            // Another process created it at the same time: use that one (it may still be writing it).
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (ReadKey() is { } theirs) return _key = theirs;
                    throw; // it vanished again (that process could not write it)
                }
                catch (Exception e) when (attempt < 10 && e is CryptographicException or IOException)
                {
                    Thread.Sleep(20); // still being written (or still open on Windows)
                }
            }
        }
        using (stream)
        {
            try
            {
                stream.Write(key);
                stream.Flush(flushToDisk: true);
            }
            catch (IOException)
            {
                // This process made the file: never leave a partial key behind.
                stream.Dispose();
                File.Delete(keyFile);
                throw;
            }
        }
        return _key = key;
    }

    /// <summary>The existing key, or null when there is no key file. Throws when it exists with the wrong size.</summary>
    byte[]? ReadKey()
    {
        byte[] existing;
        try
        {
            existing = File.ReadAllBytes(keyFile);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        return existing.Length == KeySize ? existing : throw new CryptographicException(DamagedMessage);
    }

    /// <summary>Unprotect: the existing key, or null when it is missing, unreadable or damaged. Never creates it.</summary>
    byte[]? KeyIfAny()
    {
        if (_key is not null) return _key;
        try
        {
            return ReadKey() is { } existing ? _key = existing : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Owner-only from the moment it exists on Unix (no window with wider permissions).</summary>
    static FileStreamOptions CreateOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }
}
