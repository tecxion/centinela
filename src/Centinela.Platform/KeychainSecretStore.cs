using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>
/// Passwords in the system keychain (Secret Service on Linux, the Keychain on macOS) when there is one; otherwise in
/// the encrypted file of <paramref name="fallback"/>. cameras.json keeps "<paramref name="prefix"/>&lt;Id&gt;" or the
/// "file:" value.
/// </summary>
internal sealed class KeychainSecretStore(string prefix, ISecretBackend? backend, FileSecretStore fallback) : ISecretStore
{
    volatile bool _usesFileFallback;

    /// <summary>The prefix of the references it keeps ("secret-service:" or "keychain:").</summary>
    public string Prefix => prefix;

    /// <summary>True once a password was saved to (or read from) the encrypted file: the UI tells the user once.</summary>
    public bool UsesFileFallback => _usesFileFallback;

    public string Protect(Guid cameraId, string password)
    {
        if (password.Length == 0) return "";
        if (backend is not null)
        {
            if (backend.Store(cameraId, password)) return SecretReference.For(prefix, cameraId);
            // The camera may have an older password in the keychain: do not leave it behind.
            backend.Delete(cameraId);
        }
        _usesFileFallback = true;
        return fallback.Protect(cameraId, password);
    }

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        var (valuePrefix, _) = SecretPrefix.Split(storedValue);
        if (valuePrefix == prefix)
            return SecretReference.CameraOf(prefix, storedValue) == cameraId ? backend?.Lookup(cameraId) : null;
        if (valuePrefix != SecretPrefix.File) return null; // DPAPI (Windows) or the other system's keychain
        _usesFileFallback = true;
        return fallback.Unprotect(cameraId, storedValue);
    }

    public void Forget(Guid cameraId, string storedValue)
    {
        if (SecretReference.CameraOf(prefix, storedValue) == cameraId) backend?.Delete(cameraId);
    }
}
