using Centinela.Core.Platform;
using Centinela.Platform.Linux;

namespace Centinela.Platform.Tests;

public sealed class KeychainSecretStoreTests : IDisposable
{
    const string SecretService = SecretPrefix.SecretService; // Linux
    const string Keychain = SecretPrefix.Keychain;           // macOS

    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-keychain-secrets-" + Guid.NewGuid());
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
        var store = new KeychainSecretStore(SecretService, backend, Fallback);
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
        var store = new KeychainSecretStore(SecretService, new FakeBackend { Fails = true }, Fallback);
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
        var store = new KeychainSecretStore(SecretService, null, Fallback);
        var id = Guid.NewGuid();

        var stored = store.Protect(id, "SuperSecreta123");

        Assert.StartsWith("file:", stored);
        Assert.True(store.UsesFileFallback);
        Assert.Equal("SuperSecreta123", store.Unprotect(id, stored));
        // A keychain reference cannot be read without a keychain.
        Assert.Null(store.Unprotect(id, SecretReference.For(SecretService, id)));
    }

    [Fact]
    public void Reading_a_file_value_also_means_the_file_is_in_use()
    {
        var id = Guid.NewGuid();
        var stored = new KeychainSecretStore(SecretService, null, Fallback).Protect(id, "x");
        var store = new KeychainSecretStore(SecretService, new FakeBackend(), Fallback);

        Assert.False(store.UsesFileFallback);
        Assert.Equal("x", store.Unprotect(id, stored));
        Assert.True(store.UsesFileFallback);
    }

    [Fact]
    public void Empty_passwords_stay_empty_and_touch_nothing()
    {
        var backend = new FakeBackend();
        var store = new KeychainSecretStore(SecretService, backend, Fallback);
        var id = Guid.NewGuid();

        Assert.Equal("", store.Protect(id, ""));
        Assert.Equal("", store.Unprotect(id, ""));
        Assert.Empty(backend.Items);
        Assert.False(store.UsesFileFallback);
    }

    [Fact]
    public void Values_from_other_systems_cannot_be_read_here()
    {
        var store = new KeychainSecretStore(SecretService, new FakeBackend(), Fallback);
        var id = Guid.NewGuid();

        Assert.Null(store.Unprotect(id, "dpapi:QUJD"));
        Assert.Null(store.Unprotect(id, "QUJD"));                      // Centinela 1.3 (DPAPI, no prefix)
        Assert.Null(store.Unprotect(id, "keychain:" + id.ToString("D"))); // macOS
    }

    [Fact]
    public void A_missing_keychain_item_reads_as_null()
    {
        var store = new KeychainSecretStore(SecretService, new FakeBackend(), Fallback);
        var id = Guid.NewGuid();
        Assert.Null(store.Unprotect(id, SecretReference.For(SecretService, id)));
    }

    [Fact]
    public void Forget_deletes_only_keychain_items()
    {
        var backend = new FakeBackend();
        var store = new KeychainSecretStore(SecretService, backend, Fallback);
        var id = Guid.NewGuid();
        var stored = store.Protect(id, "x");

        store.Forget(id, "file:AAAA");
        store.Forget(id, "");
        store.Forget(id, "dpapi:QUJD");
        Assert.Empty(backend.Deleted);

        store.Forget(id, stored);
        Assert.Equal([id], backend.Deleted);
        Assert.Empty(backend.Items);

        new KeychainSecretStore(SecretService, null, Fallback).Forget(id, stored); // no keychain: nothing to do, no exception
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
    }

    [Fact]
    public void References_name_the_prefix_and_the_camera()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal("secret-service:0f8fad5b-d9cb-469f-a165-70867728950e", SecretReference.For(SecretService, id));
        Assert.Equal("keychain:0f8fad5b-d9cb-469f-a165-70867728950e", SecretReference.For(Keychain, id));
        Assert.Equal(id, SecretReference.CameraOf(SecretService, "secret-service:0f8fad5b-d9cb-469f-a165-70867728950e"));
        Assert.Equal(id, SecretReference.CameraOf(Keychain, "keychain:0f8fad5b-d9cb-469f-a165-70867728950e"));
        Assert.Null(SecretReference.CameraOf(Keychain, "secret-service:0f8fad5b-d9cb-469f-a165-70867728950e"));
        Assert.Null(SecretReference.CameraOf(SecretService, "keychain:0f8fad5b-d9cb-469f-a165-70867728950e"));
        Assert.Null(SecretReference.CameraOf(SecretService, "secret-service:no-es-un-id"));
        Assert.Null(SecretReference.CameraOf(Keychain, "keychain:0f8fad5bd9cb469fa16570867728950e")); // not "D"
        Assert.Null(SecretReference.CameraOf(SecretService, "file:AAAA"));
        Assert.Null(SecretReference.CameraOf(SecretService, "0f8fad5b-d9cb-469f-a165-70867728950e"));
    }

    [Fact]
    public void With_the_keychain_prefix_the_reference_is_keychain()
    {
        var backend = new FakeBackend();
        var store = new KeychainSecretStore(Keychain, backend, Fallback);
        var id = Guid.NewGuid();

        var stored = store.Protect(id, "SuperSecreta123");

        Assert.Equal("keychain:" + id.ToString("D"), stored);
        Assert.Equal("SuperSecreta123", backend.Items[id]);
        Assert.Equal("SuperSecreta123", store.Unprotect(id, stored));
        Assert.False(store.UsesFileFallback);

        store.Forget(id, stored);
        Assert.Equal([id], backend.Deleted);
        Assert.Empty(backend.Items);
    }

    [Fact]
    public void Each_store_reads_and_forgets_only_its_own_keychain_references()
    {
        var backend = new FakeBackend();
        var id = Guid.NewGuid();
        backend.Items[id] = "x"; // the item is there: only the prefix keeps it from being read
        var mac = new KeychainSecretStore(Keychain, backend, Fallback);
        var linux = new KeychainSecretStore(SecretService, backend, Fallback);

        Assert.Null(mac.Unprotect(id, "secret-service:" + id.ToString("D")));
        Assert.Null(linux.Unprotect(id, "keychain:" + id.ToString("D")));

        mac.Forget(id, "secret-service:" + id.ToString("D"));
        linux.Forget(id, "keychain:" + id.ToString("D"));
        Assert.Empty(backend.Deleted);
    }

    [Fact]
    public void When_the_keychain_fails_the_cameras_old_item_is_removed()
    {
        var backend = new FakeBackend();
        var store = new KeychainSecretStore(SecretService, backend, Fallback);
        var id = Guid.NewGuid();
        store.Protect(id, "vieja");
        backend.Fails = true;

        var stored = store.Protect(id, "nueva");

        Assert.StartsWith("file:", stored);
        Assert.Equal([id], backend.Deleted);
        Assert.Empty(backend.Items);
        Assert.Equal("nueva", store.Unprotect(id, stored));
    }

    [Fact]
    public void Forget_leaves_another_cameras_keychain_item_alone()
    {
        var backend = new FakeBackend();
        var store = new KeychainSecretStore(SecretService, backend, Fallback);
        var other = Guid.NewGuid();
        var stored = store.Protect(other, "x");

        store.Forget(Guid.NewGuid(), stored);

        Assert.Empty(backend.Deleted);
        Assert.Equal("x", backend.Items[other]);
    }

    [Fact]
    public void A_turned_down_prompt_is_not_shown_again_until_the_keychain_is_unlocked()
    {
        var refusal = new PromptRefusal();
        Assert.True(refusal.MayPrompt);

        refusal.Record(accepted: false); // dismissed or timed out
        Assert.False(refusal.MayPrompt);

        refusal.Unlocked(); // unlocked meanwhile, e.g. from the keychain app
        Assert.True(refusal.MayPrompt);

        refusal.Record(accepted: false);
        refusal.Record(accepted: true);
        Assert.True(refusal.MayPrompt);
    }

    [Fact]
    public async Task A_service_that_does_not_answer_is_not_called_again_until_it_answers()
    {
        var timeouts = new ServiceTimeouts();
        Assert.False(timeouts.Silent);

        await Assert.ThrowsAsync<TimeoutException>(() => timeouts.Watch<bool>(() => throw new TimeoutException()));
        Assert.True(timeouts.Silent); // the next operations give false/null at once

        Assert.Equal(7, await timeouts.Watch(() => Task.FromResult(7))); // an answer (e.g. one already under way)
        Assert.False(timeouts.Silent);
    }

    [Fact]
    public async Task An_error_reply_or_a_turned_down_prompt_is_not_a_silent_service()
    {
        var timeouts = new ServiceTimeouts();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            timeouts.Watch<bool>(() => throw new InvalidOperationException()));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            timeouts.Watch<bool>(() => throw new OperationCanceledException()));
        Assert.False(await timeouts.Watch(() => Task.FromResult(false))); // e.g. the prompt was dismissed
        Assert.False(timeouts.Silent);
    }

    [Fact]
    public void A_keychain_reference_for_another_camera_is_not_read()
    {
        var backend = new FakeBackend();
        var store = new KeychainSecretStore(SecretService, backend, Fallback);
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        store.Protect(other, "x");

        Assert.Null(store.Unprotect(id, SecretReference.For(SecretService, other)));
    }
}
