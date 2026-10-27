using System.Globalization;
using Centinela.Core.Onvif;

namespace Centinela.Desktop.Services.Quality;

/// <summary>Rules of the «Cambiar calidad (ONVIF)» editors: choices, limits, reading and the light recommendation.</summary>
static class QualityEdits
{
    public const int MinBitrate = 32;
    public const int MaxBitrate = 16384;

    /// <summary>The fps the camera accepts: at least 1, and 30 at most when it does not say.</summary>
    public static (int Min, int Max) FpsRange(EncoderOptions? options) =>
        (Math.Max(1, options?.FpsMin ?? 1), options is { FpsMax: > 0 } o ? o.FpsMax : 30);

    /// <summary>The resolutions it accepts, with the current one first when it is not among them.</summary>
    public static IReadOnlyList<(int W, int H)> Choices(EncoderSettings current, EncoderOptions? options)
    {
        var choices = (options?.Resolutions ?? []).Select(r => (r.Width, r.Height)).ToList();
        if (!choices.Contains((current.Width, current.Height))) choices.Insert(0, (current.Width, current.Height));
        return choices;
    }

    /// <summary>Light values for a weak link: main about 720p, sub about 360p, 10 fps, 768 / 256 kbps.</summary>
    public static EncoderChange Recommended(IReadOnlyList<(int W, int H)> choices, EncoderOptions? options, bool main)
    {
        var maxHeight = main ? 720 : 360;
        var fitting = choices.Where(r => r.H <= maxHeight).OrderByDescending(r => r.W * r.H).ToList();
        var (w, h) = fitting.Count > 0 ? fitting[0] : choices.OrderBy(r => r.W * r.H).First();
        var (fpsMin, fpsMax) = FpsRange(options);
        var fps = Math.Min(Math.Max(10, fpsMin), fpsMax);
        return new EncoderChange(w, h, fps, main ? 768 : 256);
    }

    /// <summary>Reads one editor row; null when valid (<paramref name="change"/> set), otherwise the warning to show.</summary>
    public static string? TryRead(string token, (int W, int H)? resolution, string fpsText, string bitrateText,
        EncoderOptions? options, out EncoderChange change)
    {
        change = new EncoderChange(0, 0, 0, 0);
        if (resolution is not var (w, h)
            || !int.TryParse(fpsText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var fps)
            || !int.TryParse(bitrateText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var kbps))
            return $"Revisa los valores de {token}: fps y bitrate deben ser números enteros.";
        var (fpsMin, fpsMax) = FpsRange(options);
        if (fps < fpsMin || fps > fpsMax || kbps < MinBitrate || kbps > MaxBitrate)
            return $"{token}: los fps deben estar entre {fpsMin} y {fpsMax}, y el bitrate entre {MinBitrate} y {MaxBitrate} kbps.";
        change = new EncoderChange(w, h, fps, kbps);
        return null;
    }
}
