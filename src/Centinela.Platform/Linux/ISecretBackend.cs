namespace Centinela.Platform.Linux;

/// <summary>The system keychain as <see cref="LinuxSecretStore"/> needs it. Never throws: failures are false/null.</summary>
internal interface ISecretBackend
{
    /// <summary>Saves (or replaces) the camera's password; false when the keychain could not take it.</summary>
    bool Store(Guid cameraId, string password);

    /// <summary>The camera's password; null when it is not there or the keychain cannot be read.</summary>
    string? Lookup(Guid cameraId);

    void Delete(Guid cameraId);
}
