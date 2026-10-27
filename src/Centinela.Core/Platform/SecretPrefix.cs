namespace Centinela.Core.Platform;

/// <summary>
/// The method a stored password was saved with. A value without a known prefix is a Centinela 1.3 value
/// (DPAPI, Windows).
/// </summary>
public static class SecretPrefix
{
    public const string Dpapi = "dpapi:";
    public const string Keychain = "keychain:";
    public const string SecretService = "secret-service:";
    public const string File = "file:";

    static readonly string[] Known = [Dpapi, Keychain, SecretService, File];

    public static (string Prefix, string Payload) Split(string stored)
    {
        foreach (var prefix in Known)
            if (stored.StartsWith(prefix, StringComparison.Ordinal)) return (prefix, stored[prefix.Length..]);
        return ("", stored);
    }
}
