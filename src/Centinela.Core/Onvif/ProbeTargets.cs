using System.Net;
using System.Net.Sockets;

namespace Centinela.Core.Onvif;

/// <summary>
/// Addresses to probe one by one (unicast WS-Discovery), for cameras on another subnet: the multicast probe
/// never crosses a router. Text form, separated by commas, semicolons or spaces: <c>10.20.30.0/24</c>,
/// <c>10.20.30.*</c>, <c>10.20.30.1-50</c> or a single <c>10.20.30.118</c>. IPv4 only, /22 or smaller.
/// </summary>
public static class ProbeTargets
{
    /// <summary>More than this is refused: probing is meant for a few home subnets, not for scanning.</summary>
    public const int MaxAddresses = 16384;
    const int MinPrefix = 22;

    /// <summary>The distinct /24 networks of the given hosts (IPv4 literals only), as <c>a.b.c.0/24</c>.</summary>
    public static string FromHosts(IEnumerable<string> hosts) => string.Join(", ", NetworksOf(hosts));

    /// <summary>The distinct /24 networks (<c>a.b.c.0/24</c>) of the given hosts, sorted; non-IPv4 hosts are skipped.</summary>
    public static IReadOnlyList<string> NetworksOf(IEnumerable<string> hosts) =>
        hosts.Select(NetworkOf).OfType<string>().Distinct().OrderBy(SortKey).ToList();

    /// <summary>The /24 network of an IPv4 literal as <c>a.b.c.0/24</c>, or null.</summary>
    public static string? NetworkOf(string host) =>
        IPAddress.TryParse(host.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            ? $"{ip.GetAddressBytes()[0]}.{ip.GetAddressBytes()[1]}.{ip.GetAddressBytes()[2]}.0/24"
            : null;

    /// <summary>
    /// Every host (.1–.254) of every /24 in the /16 around <paramref name="local"/>: where home and small-office
    /// networks put their other subnets (192.168.x, 10.a.x). 65 024 addresses, for «Detectar otras redes».
    /// </summary>
    public static IReadOnlyList<IPAddress> Zone(IPAddress local)
    {
        var b = local.GetAddressBytes();
        var result = new List<IPAddress>(256 * 254);
        for (var third = 0; third < 256; third++)
            for (var fourth = 1; fourth < 255; fourth++)
                result.Add(new IPAddress([b[0], b[1], (byte)third, (byte)fourth]));
        return result;
    }

    static uint SortKey(string network) =>
        TryAddress(network.Split('/')[0], out var value) ? value : uint.MaxValue;

    /// <summary>
    /// The addresses described by <paramref name="text"/>, without network and broadcast addresses of ranges.
    /// Throws <see cref="FormatException"/> with a Spanish message for an entry it cannot understand or a
    /// total above <see cref="MaxAddresses"/>.
    /// </summary>
    public static IReadOnlyList<IPAddress> Parse(string? text)
    {
        var result = new List<IPAddress>();
        var seen = new HashSet<uint>();
        foreach (var entry in (text ?? "").Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var value in Expand(entry))
            {
                if (!seen.Add(value)) continue;
                if (seen.Count > MaxAddresses)
                    throw new FormatException($"Demasiadas direcciones (más de {MaxAddresses}). Usa redes /24 o más pequeñas.");
                result.Add(ToAddress(value));
            }
        }
        return result;
    }

    static IEnumerable<uint> Expand(string entry)
    {
        if (entry.EndsWith(".*", StringComparison.Ordinal)) entry = entry[..^2] + ".0/24";

        var slash = entry.IndexOf('/');
        if (slash >= 0)
        {
            if (!TryAddress(entry[..slash], out var network) || !int.TryParse(entry[(slash + 1)..], out var prefix)
                || prefix is < MinPrefix or > 32)
                throw Invalid(entry);
            if (prefix == 32) return [network];
            var mask = uint.MaxValue << (32 - prefix);
            var first = (network & mask) + 1;
            var last = (network | ~mask) - 1;
            return Range(first, last);
        }

        var dash = entry.IndexOf('-');
        if (dash >= 0)
        {
            if (!TryAddress(entry[..dash], out var start)) throw Invalid(entry);
            var end = entry[(dash + 1)..];
            uint stop;
            if (byte.TryParse(end, out var lastOctet)) stop = (start & 0xFFFFFF00) | lastOctet;
            else if (!TryAddress(end, out stop)) throw Invalid(entry);
            if (stop < start || stop - start >= MaxAddresses) throw Invalid(entry);
            return Range(start, stop);
        }

        return TryAddress(entry, out var single) ? [single] : throw Invalid(entry);
    }

    static IEnumerable<uint> Range(uint first, uint last)
    {
        for (var value = first; value <= last && value >= first; value++) yield return value;
    }

    static bool TryAddress(string text, out uint value)
    {
        value = 0;
        if (text.Count(c => c == '.') != 3 || !IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        value = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        return true;
    }

    static IPAddress ToAddress(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    static FormatException Invalid(string entry) =>
        new($"No entiendo «{entry}». Escribe redes como 10.20.30.0/24, 10.20.30.* o 10.20.30.1-50.");
}
