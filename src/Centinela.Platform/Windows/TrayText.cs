namespace Centinela.Platform.Windows;

/// <summary>
/// Fits texts to the fixed buffers of NOTIFYICONDATAW (szTip 128, szInfoTitle 64, szInfo 256 characters, each
/// including the terminator); longer texts would be cut silently by the marshaller.
/// </summary>
public static class TrayText
{
    public const int TipMax = 127;
    public const int TitleMax = 63;
    public const int InfoMax = 255;

    /// <summary>
    /// At most <paramref name="max"/> characters, ending in «…» when cut. Never empty: an empty balloon text makes
    /// Windows remove the balloon instead of showing it, so (like Centinela 1.3) it becomes a space.
    /// </summary>
    public static string Fit(string text, int max)
    {
        if (text.Length == 0) return " ";
        if (text.Length <= max) return text;
        var cut = max - 1;
        // Do not split a surrogate pair.
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return string.Concat(text.AsSpan(0, cut), "…");
    }
}
