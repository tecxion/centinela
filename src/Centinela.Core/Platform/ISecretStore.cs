namespace Centinela.Core.Platform;

/// <summary>
/// Where a camera's password lives: the value returned by <see cref="Protect"/> is what cameras.json keeps (the
/// encrypted password itself, or only a reference when the password sits in the system keychain).
/// </summary>
public interface ISecretStore
{
    /// <summary>Stores the password; returns the value to keep in cameras.json ("" for an empty password).</summary>
    string Protect(Guid cameraId, string password);

    /// <summary>The password behind a stored value; "" for an empty value; null when this system cannot read it.</summary>
    string? Unprotect(Guid cameraId, string storedValue);

    /// <summary>Forgets a deleted camera's password. No-op when nothing is held outside the stored value.</summary>
    void Forget(Guid cameraId, string storedValue);
}
