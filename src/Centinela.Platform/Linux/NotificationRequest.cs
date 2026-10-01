namespace Centinela.Platform.Linux;

/// <summary>
/// The arguments of org.freedesktop.Notifications.Notify (susssasa{sv}i), hints aside (always empty). Pure: no D-Bus.
/// </summary>
public sealed record NotificationRequest(
    string AppName, uint ReplacesId, string AppIcon, string Summary, string Body,
    IReadOnlyList<string> Actions, int ExpireTimeout)
{
    /// <summary>The action key the server reports when the notification itself is clicked.</summary>
    public const string DefaultAction = "default";
    public const int MaxBodyLength = 1000;
    const string AppTitle = "Centinela";

    /// <summary>
    /// A new notification (replaces none) with the app icon, the default action labelled «Abrir» and the server's
    /// own expiry. An empty title becomes «Centinela»; the text is cut to <see cref="MaxBodyLength"/> characters.
    /// </summary>
    public static NotificationRequest Create(string? title, string? text) =>
        new(AppTitle, 0, "centinela", string.IsNullOrWhiteSpace(title) ? AppTitle : title, Cut(text ?? ""),
            [DefaultAction, "Abrir"], -1);

    /// <summary>At most <see cref="MaxBodyLength"/> characters, without splitting a surrogate pair.</summary>
    static string Cut(string text)
    {
        if (text.Length <= MaxBodyLength) return text;
        var length = char.IsHighSurrogate(text[MaxBodyLength - 1]) ? MaxBodyLength - 1 : MaxBodyLength;
        return text[..length];
    }
}
