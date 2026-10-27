using System.Globalization;
using System.Xml.Linq;

namespace Centinela.Core.Onvif;

/// <summary>One video stream's encoder settings as a camera reports them (0 / null = not reported).</summary>
public sealed record EncoderSettings(
    string Name, string Codec, int Width, int Height, int Fps, int BitrateKbps, string? RateControl, int Gop);

/// <summary>What an ONVIF encoder configuration accepts (0 = not reported).</summary>
public sealed record EncoderOptions(
    IReadOnlyList<(int Width, int Height)> Resolutions, int FpsMin, int FpsMax, int BitrateMinKbps, int BitrateMaxKbps);

/// <summary>Parsers for the encoder settings of ONVIF (Media 1) and of the Dahua/Imou HTTP API. Read-only.</summary>
public static class EncoderSettingsParser
{
    /// <summary>GetVideoEncoderConfigurationsResponse → one entry per configuration (with its token).</summary>
    public static IReadOnlyList<(string Token, EncoderSettings Settings)> ParseOnvifConfigurations(string xml) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "Configurations" && Child(e, "Encoding") is not null)
            .Select(c =>
            {
                var resolution = Child(c, "Resolution");
                var rate = Child(c, "RateControl");
                var gop = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "GovLength");
                return ((string?)c.Attribute("token") ?? "", new EncoderSettings(
                    Child(c, "Name")?.Value.Trim() ?? "",
                    Child(c, "Encoding")?.Value.Trim() ?? "",
                    Int(Child(resolution, "Width")), Int(Child(resolution, "Height")),
                    Int(Child(rate, "FrameRateLimit")), Int(Child(rate, "BitrateLimit")),
                    null, Int(gop)));
            })
            .ToList();

    /// <summary>GetVideoEncoderConfigurationOptionsResponse → resolutions and ranges (first ones found).</summary>
    public static EncoderOptions ParseOnvifOptions(string xml)
    {
        var doc = XDocument.Parse(xml);
        var resolutions = doc.Descendants()
            .Where(e => e.Name.LocalName == "ResolutionsAvailable")
            .Select(r => (Int(Child(r, "Width")), Int(Child(r, "Height"))))
            .Where(r => r.Item1 > 0 && r.Item2 > 0)
            .Distinct()
            .OrderByDescending(r => r.Item1 * r.Item2)
            .ToList();
        var fps = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "FrameRateRange");
        var bitrate = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "BitrateRange");
        return new EncoderOptions(resolutions, Int(Child(fps, "Min")), Int(Child(fps, "Max")),
            Int(Child(bitrate, "Min")), Int(Child(bitrate, "Max")));
    }

    /// <summary>
    /// Dahua <c>configManager.cgi?action=getConfig&amp;name=Encode</c> → the main (MainFormat[0]) and sub
    /// (ExtraFormat[0]) streams of the first channel, when present.
    /// </summary>
    public static IReadOnlyList<EncoderSettings> ParseDahuaEncode(string text)
    {
        var values = text.Split('\n')
            .Select(line => line.Trim())
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0])
            .ToDictionary(g => g.Key, g => g.First()[1].Trim());

        var result = new List<EncoderSettings>();
        foreach (var (format, name) in new[] { ("MainFormat[0]", "Principal"), ("ExtraFormat[0]", "Secundario") })
        {
            var prefix = $"table.Encode[0].{format}.Video.";
            if (!values.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal))) continue;
            string? Get(string key) => values.GetValueOrDefault(prefix + key);
            result.Add(new EncoderSettings(name, Get("Compression") ?? "",
                ParseInt(Get("Width")), ParseInt(Get("Height")), ParseInt(Get("FPS")), ParseInt(Get("BitRate")),
                Get("BitRateControl"), ParseInt(Get("GOP"))));
        }
        return result;
    }

    static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    static int Int(XElement? element) => ParseInt(element?.Value);

    static int ParseInt(string? value) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? (int)Math.Round(number) : 0;
}
