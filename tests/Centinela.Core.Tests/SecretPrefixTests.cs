using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

public class SecretPrefixTests
{
    [Theory]
    [InlineData("dpapi:QUJD", "dpapi:", "QUJD")]
    [InlineData("keychain:8b1f", "keychain:", "8b1f")]
    [InlineData("secret-service:8b1f", "secret-service:", "8b1f")]
    [InlineData("file:AAAA", "file:", "AAAA")]
    [InlineData("QUJD", "", "QUJD")]          // 1.3 files: plain DPAPI base64
    [InlineData("other:x", "", "other:x")]
    [InlineData("", "", "")]
    public void Split_separates_known_prefixes(string stored, string prefix, string payload) =>
        Assert.Equal((prefix, payload), SecretPrefix.Split(stored));
}
