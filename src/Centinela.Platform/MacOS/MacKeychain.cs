using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Centinela.Platform.MacOS;

/// <summary>
/// The macOS Keychain (the user's default keychain): one generic password per camera, service <see cref="Service"/>,
/// account the camera Id ("D"), the password in UTF-8. Never throws: any failure (a missing framework included) is
/// false/null, and the store then falls back to its file.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeychain : ISecretBackend
{
    // shortcut: SecKeychain* (obsoleta desde 10.10, sigue en macOS 26); pasar a SecItem* si Apple la retira.
    const string Service = "es.tecxart.centinela";
    const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const int errSecSuccess = 0;
    const int errSecItemNotFound = -25300;

    static readonly byte[] ServiceName = Encoding.UTF8.GetBytes(Service);

    // macOS may show its Keychain permission prompt during a call (it blocks until the user answers): no time limit.
    public bool Store(Guid cameraId, string password)
    {
        var secret = Encoding.UTF8.GetBytes(password);
        try
        {
            var account = Account(cameraId);
            var found = FindItem(account, out var item);
            if (found == errSecSuccess)
            {
                try
                {
                    return SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret)
                        == errSecSuccess;
                }
                finally
                {
                    Release(item);
                }
            }
            return found == errSecItemNotFound
                && SecKeychainAddGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName,
                    (uint)account.Length, account, (uint)secret.Length, secret, IntPtr.Zero) == errSecSuccess;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public string? Lookup(Guid cameraId)
    {
        try
        {
            var account = Account(cameraId);
            if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)account.Length,
                    account, out var length, out var data, out var item) != errSecSuccess)
                return null;
            try
            {
                // Copied straight into the string: no managed copy of the password bytes is left behind.
                return data == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(data, checked((int)length));
            }
            finally
            {
                if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
                Release(item);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Delete(Guid cameraId)
    {
        try
        {
            if (FindItem(Account(cameraId), out var item) != errSecSuccess) return;
            try { SecKeychainItemDelete(item); }
            finally { Release(item); }
        }
        catch (Exception) { /* nothing to remove, or no keychain to remove it from */ }
    }

    static byte[] Account(Guid cameraId) => Encoding.UTF8.GetBytes(cameraId.ToString("D"));

    /// <summary>Finds the camera's item without reading its password, so macOS asks for no permission.</summary>
    static int FindItem(byte[] account, out IntPtr item) =>
        SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)account.Length, account,
            IntPtr.Zero, IntPtr.Zero, out item);

    // CFRelease(NULL) crashes the process.
    static void Release(IntPtr item)
    {
        if (item != IntPtr.Zero) CFRelease(item);
    }

    [DllImport(SecurityLibrary)]
    static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    /// <summary>The same call with null password pointers: finds the item without reading its password.</summary>
    [DllImport(SecurityLibrary)]
    static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, IntPtr passwordLength, IntPtr passwordData, out IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(SecurityLibrary)]
    static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    static extern void CFRelease(IntPtr cf);
}
