using System.Globalization;

namespace Centinela.Core;

public sealed record UrlCredentials(string User, string Password);

public static class StreamUrlBuilder
{
    const int DefaultRtspPort = 554;

    public static string Build(Camera camera, StreamKind kind)
    {
        var overrideUrl = kind == StreamKind.Main
            ? camera.MainUrlOverride
            : string.IsNullOrWhiteSpace(camera.SubUrlOverride) ? camera.MainUrlOverride : camera.SubUrlOverride;
        if (!string.IsNullOrWhiteSpace(overrideUrl))
            return InjectCredentials(overrideUrl.Trim(), camera.User, camera.Password);

        var path = (camera.Brand, kind) switch
        {
            (Brand.Tapo, StreamKind.Main) => "/stream1",
            (Brand.Tapo, StreamKind.Sub) => "/stream2",
            (Brand.Imou, StreamKind.Main) => "/cam/realmonitor?channel=1&subtype=0",
            (Brand.Imou, StreamKind.Sub) => "/cam/realmonitor?channel=1&subtype=1",
            _ => throw new InvalidOperationException("A custom camera needs a main RTSP URL."),
        };
        return $"rtsp://{Credentials(camera.User, camera.Password)}{camera.Host}:{camera.Port}{path}";
    }

    /// <summary>Adds user:password@ to a URL that has no credentials yet.</summary>
    public static string InjectCredentials(string url, string user, string password)
    {
        if (user.Length == 0 || !TrySplit(url, out var parts) || parts.UserInfo is not null) return url;
        return (parts with { UserInfo = Credentials(user, password)[..^1] }).ToString();
    }

    /// <summary>Removes user:password@ from a URL; the decoded values are returned in <paramref name="credentials"/>.</summary>
    public static string StripCredentials(string url, out UrlCredentials? credentials)
    {
        credentials = null;
        if (!TrySplit(url, out var parts) || parts.UserInfo is not { } userInfo) return url;
        var colon = userInfo.IndexOf(':');
        var user = colon < 0 ? userInfo : userInfo[..colon];
        var password = colon < 0 ? "" : userInfo[(colon + 1)..];
        credentials = new UrlCredentials(Uri.UnescapeDataString(user), Uri.UnescapeDataString(password));
        return (parts with { UserInfo = null }).ToString();
    }

    /// <summary>
    /// Moves a URL that points at the camera's old host to its new host. The port changes too when it
    /// equals the old one (a URL without a port counts as RTSP's default 554). Other URLs are returned unchanged.
    /// </summary>
    public static string RebaseOverride(string url, string oldHost, int oldPort, string newHost, int newPort)
    {
        if (oldHost.Length == 0 || newHost.Length == 0 || !TrySplit(url, out var parts)
            || !string.Equals(parts.Host, oldHost, StringComparison.OrdinalIgnoreCase))
            return url;
        int? port = parts.Port is null ? DefaultRtspPort
            : int.TryParse(parts.Port, NumberStyles.None, CultureInfo.InvariantCulture, out var explicitPort) ? explicitPort : null;
        var rebased = parts with { Host = newHost };
        if (port == oldPort && newPort != oldPort) rebased = rebased with { Port = newPort.ToString(CultureInfo.InvariantCulture) };
        return rebased.ToString();
    }

    static string Credentials(string user, string password) =>
        user.Length == 0 ? "" : $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@";

    /// <summary>scheme://[userinfo@]host[:port][rest], split without any re-escaping of the other parts.</summary>
    sealed record UrlParts(string Scheme, string? UserInfo, string Host, string? Port, string Rest)
    {
        public override string ToString() =>
            $"{Scheme}://{(UserInfo is null ? "" : UserInfo + "@")}{Host}{(Port is null ? "" : ":" + Port)}{Rest}";
    }

    static bool TrySplit(string url, out UrlParts parts)
    {
        parts = null!;
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0) return false;
        var rest = url[(schemeEnd + 3)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];
        var tail = end < 0 ? "" : rest[end..];

        var at = authority.LastIndexOf('@');
        var userInfo = at < 0 ? null : authority[..at];
        var hostPort = authority[(at + 1)..];

        string host;
        string? port = null;
        var bracket = hostPort.LastIndexOf(']');
        var colon = hostPort.LastIndexOf(':');
        if (colon > bracket)
        {
            host = hostPort[..colon];
            port = hostPort[(colon + 1)..];
        }
        else host = hostPort;
        if (host.Length == 0) return false;

        parts = new UrlParts(url[..schemeEnd], userInfo, host, port, tail);
        return true;
    }
}
