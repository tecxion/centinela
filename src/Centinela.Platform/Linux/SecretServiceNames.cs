namespace Centinela.Platform.Linux;

/// <summary>
/// How a camera's password is named in the Secret Service keychain: the lookup attributes and the label people see
/// in their keychain app. The reference kept in cameras.json is <see cref="SecretReference"/>'s.
/// </summary>
public static class SecretServiceNames
{
    public const string Application = "centinela";

    public static IReadOnlyDictionary<string, string> Attributes(Guid cameraId) =>
        new Dictionary<string, string> { ["app"] = Application, ["camera"] = cameraId.ToString("D") };

    public static string Label(Guid cameraId) => $"Centinela: contraseña de la cámara {cameraId:D}";
}
