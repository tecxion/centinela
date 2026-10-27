using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

/// <summary>
/// DPAPI (current Windows user). Writes the plain base64 of Centinela 1.3, without a prefix, so both versions
/// read the same cameras.json; reads it with or without "dpapi:".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    public string Protect(Guid cameraId, string password) => password.Length == 0
        ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        var (prefix, payload) = SecretPrefix.Split(storedValue);
        if (prefix is not ("" or SecretPrefix.Dpapi)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(payload), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Another Windows user's file, or damaged: the camera loses its password.
            return null;
        }
    }

    public void Forget(Guid cameraId, string storedValue) { }
}
