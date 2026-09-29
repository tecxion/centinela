using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Centinela.Core.Onvif;

public sealed record DiscoveredDevice(string Host, string DeviceServiceUrl, string? Name, string? Hardware);

public static class WsDiscovery
{
    static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);
    const string ScopePrefix = "onvif://www.onvif.org/";

    public static string BuildProbe(Guid messageId) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
            xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
            xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <e:Header>
            <w:MessageID>uuid:{messageId}</w:MessageID>
            <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
            <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
          </e:Header>
          <e:Body>
            <d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe>
          </e:Body>
        </e:Envelope>
        """;

    public static IReadOnlyList<DiscoveredDevice> ParseProbeMatches(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (XmlException) { return []; }

        var result = new List<DiscoveredDevice>();
        foreach (var match in doc.Descendants().Where(e => e.Name.LocalName == "ProbeMatch"))
        {
            var urls = Words(Child(match, "XAddrs"))
                .Select(a => Uri.TryCreate(a, UriKind.Absolute, out var u) ? u : null)
                .OfType<Uri>()
                .ToList();
            var url = urls.FirstOrDefault(u => u.HostNameType == UriHostNameType.IPv4) ?? urls.FirstOrDefault();
            if (url is null) continue;

            var scopes = Words(Child(match, "Scopes"));
            result.Add(new DiscoveredDevice(url.Host, url.ToString(), Scope(scopes, "name"), Scope(scopes, "hardware")));
        }
        return result;
    }

    /// <summary>
    /// Multicast probe on every local network, plus the same probe sent to each of <paramref name="unicastTargets"/>
    /// (cameras on other subnets: multicast does not cross routers, a unicast probe does).
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredDevice>> ProbeAsync(TimeSpan timeout,
        IReadOnlyCollection<IPAddress>? unicastTargets = null, CancellationToken ct = default)
    {
        var payload = Encoding.UTF8.GetBytes(BuildProbe(Guid.NewGuid()));
        var found = new ConcurrentDictionary<string, DiscoveredDevice>();
        var probes = LocalIPv4Addresses().Select(local => ProbeFromAsync(local, payload, timeout, found, ct)).ToList();
        if (unicastTargets is { Count: > 0 }) probes.Add(ProbeUnicastAsync(unicastTargets, payload, timeout, found, ct));
        await Task.WhenAll(probes);
        return found.Values.OrderBy(d => SortKey(d.Host)).ToList();
    }

    static async Task ProbeUnicastAsync(IReadOnlyCollection<IPAddress> targets, byte[] payload, TimeSpan timeout,
        ConcurrentDictionary<string, DiscoveredDevice> found, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var receiving = ReceiveAsync(udp, found, cts.Token);
            // UDP may drop a datagram; send the round twice.
            for (var round = 0; round < 2; round++)
            {
                foreach (var target in targets)
                {
                    try { await udp.SendAsync(payload, new IPEndPoint(target, MulticastEndpoint.Port), cts.Token); }
                    catch (SocketException) { /* unreachable address: keep going */ }
                }
                await Task.Delay(200, cts.Token);
            }
            await receiving;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
    }

    static async Task ReceiveAsync(UdpClient udp, ConcurrentDictionary<string, DiscoveredDevice> found, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                UdpReceiveResult received;
                try { received = await udp.ReceiveAsync(ct); }
                catch (SocketException) { continue; } // an ICMP "port unreachable" from a host without ONVIF
                foreach (var device in ParseProbeMatches(Encoding.UTF8.GetString(received.Buffer)))
                    found.TryAdd(device.Host, device);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    static async Task ProbeFromAsync(IPAddress local, byte[] payload, TimeSpan timeout,
        ConcurrentDictionary<string, DiscoveredDevice> found, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            // UDP may drop the first datagram; send twice.
            await udp.SendAsync(payload, MulticastEndpoint, cts.Token);
            await Task.Delay(100, cts.Token);
            await udp.SendAsync(payload, MulticastEndpoint, cts.Token);
            while (true)
            {
                var received = await udp.ReceiveAsync(cts.Token);
                foreach (var device in ParseProbeMatches(Encoding.UTF8.GetString(received.Buffer)))
                    found.TryAdd(device.Host, device);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
    }

    static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.SupportsMulticast)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

    static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    static string[] Words(string? value) =>
        (value ?? "").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

    static string? Scope(IEnumerable<string> scopes, string key)
    {
        var prefix = ScopePrefix + key + "/";
        return scopes
            .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(s => Uri.UnescapeDataString(s[prefix.Length..]))
            .FirstOrDefault();
    }

    static uint SortKey(string host) =>
        IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            ? (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ip.GetAddressBytes()))
            : uint.MaxValue;
}
