using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

/// <summary>Reversible stand-in: "fake:" + base64. Anything else cannot be read. Records forgotten ids.</summary>
sealed class FakeSecretStore : ISecretStore
{
    public readonly List<Guid> Forgotten = [];

    public string Protect(Guid cameraId, string password) =>
        password.Length == 0 ? "" : "fake:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        if (!storedValue.StartsWith("fake:", StringComparison.Ordinal)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(storedValue["fake:".Length..])); }
        catch (FormatException) { return null; }
    }

    public void Forget(Guid cameraId, string storedValue) => Forgotten.Add(cameraId);
}
