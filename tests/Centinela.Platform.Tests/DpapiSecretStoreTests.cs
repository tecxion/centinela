using System.Runtime.Versioning;
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

// The attribute only tells the platform analyzer (CA1416) that Skip.IfNot guards every test; xUnit ignores it.
[SupportedOSPlatform("windows")]
public class DpapiSecretStoreTests
{
    static readonly Guid Id = Guid.NewGuid();

    [SkippableFact]
    public void Round_trips_without_a_prefix_so_centinela_1_3_can_still_read_it()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI solo existe en Windows");
        var store = new DpapiSecretStore();
        var stored = store.Protect(Id, "SuperSecreta123");
        Assert.DoesNotContain(":", stored);
        Assert.DoesNotContain("SuperSecreta123", stored);
        Assert.Equal("SuperSecreta123", store.Unprotect(Id, stored));
        Assert.Equal("SuperSecreta123", store.Unprotect(Id, "dpapi:" + stored));
    }

    [SkippableFact]
    public void Empty_unreadable_and_foreign_values()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI solo existe en Windows");
        var store = new DpapiSecretStore();
        Assert.Equal("", store.Protect(Id, ""));
        Assert.Equal("", store.Unprotect(Id, ""));
        Assert.Null(store.Unprotect(Id, "AAAA"));
        Assert.Null(store.Unprotect(Id, "not base64!"));
        Assert.Null(store.Unprotect(Id, "keychain:" + Id));
    }
}
