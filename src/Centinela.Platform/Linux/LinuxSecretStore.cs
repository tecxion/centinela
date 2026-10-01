using Centinela.Core.Platform;

namespace Centinela.Platform.Linux;

/// <summary>
/// Passwords in the Secret Service keychain (GNOME Keyring, KWallet) when there is one; otherwise in the encrypted
/// file of <paramref name="fallback"/>. cameras.json keeps "secret-service:&lt;Id&gt;" or the "file:" value.
/// </summary>
internal sealed class LinuxSecretStore(ISecretBackend? backend, FileSecretStore fallback) : ISecretStore
{
    volatile bool _usesFileFallback;

    /// <summary>True once a password was saved to (or read from) the encrypted file: the UI tells the user once.</summary>
    public bool UsesFileFallback => _usesFileFallback;

    public string Protect(Guid cameraId, string password)
    {
        if (password.Length == 0) return "";
        if (backend is not null && backend.Store(cameraId, password)) return SecretServiceNames.StoredValue(cameraId);
        _usesFileFallback = true;
        return fallback.Protect(cameraId, password);
    }

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        var (prefix, _) = SecretPrefix.Split(storedValue);
        if (prefix == SecretPrefix.SecretService)
            return SecretServiceNames.CameraOf(storedValue) == cameraId ? backend?.Lookup(cameraId) : null;
        if (prefix != SecretPrefix.File) return null; // DPAPI (Windows) or keychain: (macOS)
        _usesFileFallback = true;
        return fallback.Unprotect(cameraId, storedValue);
    }

    public void Forget(Guid cameraId, string storedValue)
    {
        if (SecretServiceNames.CameraOf(storedValue) == cameraId) backend?.Delete(cameraId);
    }
}
