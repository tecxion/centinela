using System.Runtime.Versioning;

namespace Centinela.Platform.Tests;

public sealed class FileSecretStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-secrets-" + Guid.NewGuid());
    string KeyFile => Path.Combine(_dir, "secret.key");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Round_trips_with_the_file_prefix_and_creates_the_key_once()
    {
        var id = Guid.NewGuid();
        var stored = new FileSecretStore(KeyFile).Protect(id, "SuperSecreta123");
        Assert.StartsWith("file:", stored);
        Assert.DoesNotContain("SuperSecreta123", stored);
        var key = File.ReadAllBytes(KeyFile);
        Assert.Equal(32, key.Length);
        // A new instance reads the same key.
        Assert.Equal("SuperSecreta123", new FileSecretStore(KeyFile).Unprotect(id, stored));
        Assert.Equal(key, File.ReadAllBytes(KeyFile));
    }

    [Fact]
    public void A_value_is_bound_to_its_camera()
    {
        var store = new FileSecretStore(KeyFile);
        var stored = store.Protect(Guid.NewGuid(), "x");
        Assert.Null(store.Unprotect(Guid.NewGuid(), stored));
    }

    [Fact]
    public void Empty_foreign_and_damaged_values()
    {
        var store = new FileSecretStore(KeyFile);
        var id = Guid.NewGuid();
        Assert.Equal("", store.Protect(id, ""));
        Assert.Equal("", store.Unprotect(id, ""));
        Assert.Null(store.Unprotect(id, "QUJD"));             // DPAPI value from Windows
        Assert.Null(store.Unprotect(id, "file:AAAA"));        // too short
        Assert.Null(store.Unprotect(id, "file:not base64!"));
    }

    [SkippableFact]
    [UnsupportedOSPlatform("windows")] // for CA1416 only: Skip.If already skips it on Windows
    public void Key_file_is_readable_only_by_its_owner_on_unix()
    {
        Skip.If(OperatingSystem.IsWindows(), "permisos Unix");
        new FileSecretStore(KeyFile).Protect(Guid.NewGuid(), "x");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile));
    }
}
