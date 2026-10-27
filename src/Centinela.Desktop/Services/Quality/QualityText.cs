using System.Globalization;
using System.Net.Http;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Media;

namespace Centinela.Desktop.Services.Quality;

/// <summary>Texts of «Calidad de la cámara…»: encoder settings, ranges and errors (never with credentials).</summary>
static class QualityText
{
    /// <summary>«Main · H264 · 1920×1080 · 25 fps · 2048 kbps CBR · GOP 50»; fields reported as 0 are left out.</summary>
    public static string Describe(EncoderSettings s)
    {
        var parts = new List<string> { s.Name.Length > 0 ? s.Name : "(sin nombre)" };
        if (s.Codec.Length > 0) parts.Add(s.Codec);
        if (s.Width > 0) parts.Add($"{s.Width}×{s.Height}");
        if (s.Fps > 0) parts.Add($"{s.Fps} fps");
        if (s.BitrateKbps > 0) parts.Add($"{s.BitrateKbps} kbps" + (s.RateControl is { Length: > 0 } rc ? $" {rc}" : ""));
        if (s.Gop > 0) parts.Add($"GOP {s.Gop}");
        return string.Join(" · ", parts);
    }

    /// <summary>«min–max», or «?» when the camera does not report it.</summary>
    public static string Range(int min, int max) =>
        max <= 0 ? "?" : string.Create(CultureInfo.InvariantCulture, $"{min}–{max}");

    public static string Explain(Exception ex) => ex switch
    {
        TaskCanceledException => "no contesta (tiempo agotado)",
        HttpRequestException http => CredentialSanitizer.Sanitize(http.Message),
        _ => CredentialSanitizer.Sanitize(ex.Message),
    };

    /// <summary>The camera's host, or that of its own stream URL when it has none.</summary>
    public static string HostOf(Camera camera)
    {
        if (camera.Host.Trim().Length > 0) return camera.Host.Trim();
        return Uri.TryCreate(camera.MainUrlOverride ?? camera.SubUrlOverride, UriKind.Absolute, out var url) ? url.Host : "";
    }

    /// <summary>«1280×720 · 10 fps · 768 kbps».</summary>
    public static string Values(EncoderChange c) => $"{c.Width}×{c.Height} · {c.Fps} fps · {c.BitrateKbps} kbps";
}
