using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace Centinela.Platform.MacOS;

/// <summary>
/// Writes and reads the launchd property list that starts Centinela at login. Pure text: no file access.
/// </summary>
public static class LaunchAgent
{
    public const string Label = "es.tecxart.centinela";

    /// <summary>The agent that runs <paramref name="exePath"/> with <c>--tray</c>, tab-indented with LF as macOS writes it.</summary>
    public static string For(string exePath) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
        "<plist version=\"1.0\">\n" +
        "<dict>\n" +
        "\t<key>Label</key>\n" +
        $"\t<string>{Label}</string>\n" +
        "\t<key>ProgramArguments</key>\n" +
        "\t<array>\n" +
        $"\t\t<string>{SecurityElement.Escape(exePath)}</string>\n" +
        "\t\t<string>--tray</string>\n" +
        "\t</array>\n" +
        "\t<key>RunAtLoad</key>\n" +
        "\t<true/>\n" +
        "</dict>\n" +
        "</plist>\n";

    /// <summary>The first <c>ProgramArguments</c> string, or null when the list cannot be read.</summary>
    public static string? ProgramOf(string plistText) =>
        Value(plistText, "ProgramArguments") is { Name.LocalName: "array" } args ? args.Element("string")?.Value : null;

    /// <summary>True when the top dictionary has <c>&lt;key&gt;Disabled&lt;/key&gt;&lt;true/&gt;</c>.</summary>
    public static bool IsDisabled(string plistText) => Value(plistText, "Disabled") is { Name.LocalName: "true" };

    /// <summary>The element after <c>&lt;key&gt;<paramref name="key"/>&lt;/key&gt;</c> in the top dictionary, or null.</summary>
    static XElement? Value(string plistText, string key)
    {
        try
        {
            // The default settings throw on the DOCTYPE; ignoring it also never fetches Apple's DTD.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(plistText), settings);
            var root = XDocument.Load(reader).Root;
            return root is { Name.LocalName: "plist" }
                ? root.Element("dict")?.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault()
                : null;
        }
        catch (XmlException) { return null; }
    }
}
