using Centinela.Core.Platform;

namespace Centinela.Desktop.Tests;

sealed class FakeSecretStore : ISecretStore
{
    public string Protect(Guid cameraId, string password) => password.Length == 0 ? "" : "fake:" + password;
    public string? Unprotect(Guid cameraId, string storedValue) =>
        storedValue.Length == 0 ? "" : storedValue.StartsWith("fake:", StringComparison.Ordinal) ? storedValue[5..] : null;
    public void Forget(Guid cameraId, string storedValue) { }
}
