namespace Centinela.Platform;

/// <summary>
/// The reference cameras.json keeps for a password in the system keychain: the keychain's prefix
/// ("secret-service:" on Linux, "keychain:" on macOS) and the camera Id.
/// </summary>
public static class SecretReference
{
    public static string For(string prefix, Guid cameraId) => prefix + cameraId.ToString("D");

    /// <summary>The camera a value with this prefix refers to; null for any other value.</summary>
    public static Guid? CameraOf(string prefix, string storedValue) =>
        storedValue.StartsWith(prefix, StringComparison.Ordinal)
        && Guid.TryParseExact(storedValue.AsSpan(prefix.Length), "D", out var id)
            ? id
            : null;
}
