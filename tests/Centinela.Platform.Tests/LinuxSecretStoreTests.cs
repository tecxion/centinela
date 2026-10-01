using Centinela.Platform.Linux;

namespace Centinela.Platform.Tests;

public sealed class LinuxSecretStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-linux-secrets-" + Guid.NewGuid());
    FileSecretStore Fallback => new(Path.Combine(_dir, "secret.key"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A keychain in memory; Store fails when <see cref="Fails"/> is set.</summary>
    sealed class FakeBackend : ISecretBackend
    {
        public readonly Dictionary<Guid, string> Items = [];
        public readonly List<Guid> Deleted = [];
        public bool Fails;

        public bool Store(Guid cameraId, string password)
        {
            if (Fails) return false;
            Items[cameraId] = password;
            return true;
        }

        public string? Lookup(Guid cameraId) => Items.GetValueOrDefault(cameraId);

        public void Delete(Guid cameraId)
        {
            Deleted.Add(cameraId);
            Items.Remove(cameraId);
        }
    }

    [Fact]
    public void With_a_keychain_the_password_goes_there_and_only_a_reference_is_kept()
    {
        var backend = new FakeBackend();
        var store = new LinuxSecretStore(backend, Fallback);
        var id = Guid.NewGuid();

        var stored = store.Protect(id, "SuperSecreta123");

        Assert.Equal("secret-service:" + id.ToString("D"), stored);
        Assert.Equal("SuperSecreta123", backend.Items[id]);
        Assert.Equal("SuperSecreta123", store.Unprotect(id, stored));
        Assert.False(store.UsesFileFallback);
        Assert.False(File.Exists(Path.Combine(_dir, "secret.key")));
    }

    [Fact]
    public void When_the_keychain_fails_the_password_goes_to_the_encrypted_file()
    {
        var store = new LinuxSecretStore(new FakeBackend { Fails = true }, Fallback);
        var id = Guid.NewGuid();

        var stored = store.Protect(id, "SuperSecreta123");

        Assert.StartsWith("file:", stored);
        Assert.DoesNotContain("SuperSecreta123", stored);
        Assert.True(store.UsesFileFallback);
        Assert.Equal("SuperSecreta123", store.Unprotect(id, stored));
    }

    [Fact]
    public void Without_a_keychain_the_password_goes_to_the_encrypted_file()
    {
        var store = new LinuxSecretStore(null, Fallback);
        var id = Guid.NewGuid();

        var stored = store.Protect(id, "SuperSecreta123");

        Assert.StartsWith("file:", stored);
        Assert.True(store.UsesFileFallback);
        Assert.Equal("SuperSecreta123", store.Unprotect(id, stored));
        // A keychain reference cannot be read without a keychain.
        Assert.Null(store.Unprotect(id, SecretServiceNames.StoredValue(id)));
    }

    [Fact]
    public void Reading_a_file_value_also_means_the_file_is_in_use()
    {
        var id = Guid.NewGuid();
        var stored = new LinuxSecretStore(null, Fallback).Protect(id, "x");
        var store = new LinuxSecretStore(new FakeBackend(), Fallback);

        Assert.False(store.UsesFileFallback);
        Assert.Equal("x", store.Unprotect(id, stored));
        Assert.True(store.UsesFileFallback);
    }

    [Fact]
    public void Empty_passwords_stay_empty_and_touch_nothing()
    {
        var backend = new FakeBackend();
        var store = new LinuxSecretStore(backend, Fallback);
        var id = Guid.NewGuid();

        Assert.Equal("", store.Protect(id, ""));
        Assert.Equal("", store.Unprotect(id, ""));
        Assert.Empty(backend.Items);
        Assert.False(store.UsesFileFallback);
    }

    [Fact]
    public void Values_from_other_systems_cannot_be_read_here()
    {
        var store = new LinuxSecretStore(new FakeBackend(), Fallback);
        var id = Guid.NewGuid();

        Assert.Null(store.Unprotect(id, "dpapi:QUJD"));
        Assert.Null(store.Unprotect(id, "QUJD"));                      // Centinela 1.3 (DPAPI, no prefix)
        Assert.Null(store.Unprotect(id, "keychain:" + id.ToString("D"))); // macOS
    }

    [Fact]
    public void A_missing_keychain_item_reads_as_null()
    {
        var store = new LinuxSecretStore(new FakeBackend(), Fallback);
        var id = Guid.NewGuid();
        Assert.Null(store.Unprotect(id, SecretServiceNames.StoredValue(id)));
    }

    [Fact]
    public void Forget_deletes_only_keychain_items()
    {
        var backend = new FakeBackend();
        var store = new LinuxSecretStore(backend, Fallback);
        var id = Guid.NewGuid();
        var stored = store.Protect(id, "x");

        store.Forget(id, "file:AAAA");
        store.Forget(id, "");
        store.Forget(id, "dpapi:QUJD");
        Assert.Empty(backend.Deleted);

        store.Forget(id, stored);
        Assert.Equal([id], backend.Deleted);
        Assert.Empty(backend.Items);

        new LinuxSecretStore(null, Fallback).Forget(id, stored); // no keychain: nothing to do, no exception
    }

    [Fact]
    public void Names_for_the_keychain()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal("centinela", SecretServiceNames.Application);
        Assert.Equal(
            new Dictionary<string, string> { ["app"] = "centinela", ["camera"] = "0f8fad5b-d9cb-469f-a165-70867728950e" },
            SecretServiceNames.Attributes(id));
        Assert.Equal("Centinela: contraseña de la cámara 0f8fad5b-d9cb-469f-a165-70867728950e", SecretServiceNames.Label(id));
        Assert.Equal("secret-service:0f8fad5b-d9cb-469f-a165-70867728950e", SecretServiceNames.StoredValue(id));
        Assert.Equal(id, SecretServiceNames.CameraOf("secret-service:0f8fad5b-d9cb-469f-a165-70867728950e"));
        Assert.Null(SecretServiceNames.CameraOf("secret-service:no-es-un-id"));
        Assert.Null(SecretServiceNames.CameraOf("file:AAAA"));
        Assert.Null(SecretServiceNames.CameraOf("0f8fad5b-d9cb-469f-a165-70867728950e"));
    }

    [Fact]
    public void A_keychain_reference_for_another_camera_is_not_read()
    {
        var backend = new FakeBackend();
        var store = new LinuxSecretStore(backend, Fallback);
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        store.Protect(other, "x");

        Assert.Null(store.Unprotect(id, SecretServiceNames.StoredValue(other)));
    }
}
