using System.Globalization;
using System.Xml.Linq;

namespace Centinela.Core.Onvif;

/// <summary>New resolution, frame rate and bitrate for one ONVIF video encoder configuration.</summary>
public sealed record EncoderChange(int Width, int Height, int Fps, int BitrateKbps);

public static class EncoderChangeBuilder
{
    const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";

    /// <summary>
    /// The body of a SetVideoEncoderConfiguration request for <paramref name="token"/>, built from the camera's
    /// own GetVideoEncoderConfigurationsResponse so every other setting (quality, profile, multicast…) is sent
    /// back unchanged. The keyframe interval keeps its length in seconds. Throws when the token is not there.
    /// </summary>
    public static string BuildSetConfiguration(string configurationsXml, string token, EncoderChange change)
    {
        var source = XDocument.Parse(configurationsXml).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Configurations" && (string?)e.Attribute("token") == token)
            ?? throw new OnvifException($"La cámara ya no tiene la configuración de vídeo «{token}».");

        var configuration = new XElement(XName.Get("Configuration", MediaNs), source.Attributes(), source.Nodes());
        var resolution = Child(configuration, "Resolution");
        Set(Child(resolution, "Width"), change.Width);
        Set(Child(resolution, "Height"), change.Height);
        var rate = Child(configuration, "RateControl");
        var oldFps = Int(Child(rate, "FrameRateLimit"));
        Set(Child(rate, "FrameRateLimit"), change.Fps);
        Set(Child(rate, "BitrateLimit"), change.BitrateKbps);
        foreach (var gop in configuration.Descendants().Where(e => e.Name.LocalName == "GovLength"))
        {
            var oldGop = Int(gop);
            if (oldGop > 0 && oldFps > 0) Set(gop, Math.Max(1, (int)Math.Round((double)oldGop / oldFps * change.Fps)));
        }

        var request = new XElement(XName.Get("SetVideoEncoderConfiguration", MediaNs),
            configuration,
            new XElement(XName.Get("ForcePersistence", MediaNs), "true"));
        return request.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>The human reason inside a SOAP fault, or null when the text is not a fault.</summary>
    public static string? FaultReason(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            if (!doc.Descendants().Any(e => e.Name.LocalName == "Fault")) return null;
            var reason = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Reason");
            var text = reason?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value.Trim();
            var subcodes = doc.Descendants().Where(e => e.Name.LocalName == "Subcode")
                .Select(s => s.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v));
            var detail = string.Join(" / ", subcodes);
            return string.Join(" — ", new[] { text, detail }.Where(s => !string.IsNullOrEmpty(s)));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    static void Set(XElement? element, int value)
    {
        if (element is not null) element.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    static int Int(XElement? element) =>
        int.TryParse(element?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
