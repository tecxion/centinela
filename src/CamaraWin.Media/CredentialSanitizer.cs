using System.Text.RegularExpressions;

namespace CamaraWin.Media;

public static partial class CredentialSanitizer
{
    [GeneratedRegex(@"([a-zA-Z][a-zA-Z0-9+.\-]*://)[^/\s@]*@")]
    private static partial Regex UserInfo();

    /// <summary>Removes "user:password@" from every URL in the text.</summary>
    public static string Sanitize(string text) => UserInfo().Replace(text, "$1");
}
