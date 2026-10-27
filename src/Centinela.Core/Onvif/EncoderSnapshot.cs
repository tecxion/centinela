using System.Globalization;

namespace Centinela.Core.Onvif;

/// <summary>
/// The encoder values a camera had before Centinela first changed them, kept on the <see cref="Camera"/> so
/// «Restaurar» can put them back later. Text form: <c>token=WxH@fps:kbps</c> entries separated by <c>;</c>.
/// </summary>
public static class EncoderSnapshot
{
    public static string Format(IEnumerable<(string Token, EncoderSettings Settings)> configurations) =>
        string.Join(";", configurations.Select(c => string.Create(CultureInfo.InvariantCulture,
            $"{c.Token}={c.Settings.Width}x{c.Settings.Height}@{c.Settings.Fps}:{c.Settings.BitrateKbps}")));

    /// <summary>Parses <see cref="Format"/>'s text; malformed entries are skipped.</summary>
    public static IReadOnlyDictionary<string, EncoderChange> Parse(string? text)
    {
        var result = new Dictionary<string, EncoderChange>();
        foreach (var entry in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = entry.LastIndexOf('=');
            if (equals <= 0) continue;
            var values = entry[(equals + 1)..].Split('x', '@', ':');
            if (values.Length == 4 && values.All(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                var n = values.Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                result[entry[..equals]] = new EncoderChange(n[0], n[1], n[2], n[3]);
            }
        }
        return result;
    }
}
