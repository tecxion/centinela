namespace CamaraWin.Core;

public static class StreamUrlBuilder
{
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
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0 || user.Length == 0) return url;
        var rest = url[(schemeEnd + 3)..];
        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        if (authority.Contains('@')) return url;
        return url[..(schemeEnd + 3)] + Credentials(user, password) + rest;
    }

    static string Credentials(string user, string password) =>
        user.Length == 0 ? "" : $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@";
}
