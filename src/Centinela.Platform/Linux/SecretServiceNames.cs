using Centinela.Core.Platform;

namespace Centinela.Platform.Linux;

/// <summary>
/// How a camera's password is named in the Secret Service keychain: the lookup attributes, the label people see
/// in their keychain app, and the reference kept in cameras.json ("secret-service:" + camera Id).
/// </summary>
public static class SecretServiceNames
{
    public const string Application = "centinela";

    public static IReadOnlyDictionary<string, string> Attributes(Guid cameraId) =>
        new Dictionary<string, string> { ["app"] = Application, ["camera"] = cameraId.ToString("D") };

    public static string Label(Guid cameraId) => $"Centinela: contraseña de la cámara {cameraId:D}";

    public static string StoredValue(Guid cameraId) => SecretPrefix.SecretService + cameraId.ToString("D");

    /// <summary>The camera a "secret-service:" value refers to; null for any other value.</summary>
    public static Guid? CameraOf(string storedValue)
    {
        var (prefix, payload) = SecretPrefix.Split(storedValue);
        return prefix == SecretPrefix.SecretService && Guid.TryParseExact(payload, "D", out var id) ? id : null;
    }
}
