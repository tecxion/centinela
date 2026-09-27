using System.Text.RegularExpressions;

namespace Centinela.Media;

public static partial class RtspStatusParser
{
    [GeneratedRegex(@"failed:\s*(\d{3})")]
    private static partial Regex MethodFailed();

    [GeneratedRegex(@"RTSP/1\.\d\s+(\d{3})")]
    private static partial Regex StatusLine();

    /// <summary>RTSP error status (≥ 400) found in an FFmpeg log line, or null.</summary>
    public static int? Parse(string line)
    {
        var match = MethodFailed().Match(line);
        if (!match.Success) match = StatusLine().Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var status) && status >= 400 ? status : null;
    }
}
