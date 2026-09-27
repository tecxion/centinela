using System.Xml.Linq;
using Centinela.Core.Onvif;

namespace Centinela.Core.Tests;

public class WsDiscoveryTests
{
    const string TapoMatch = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://www.w3.org/2003/05/soap-envelope"
            xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
            xmlns:wsdd="http://schemas.xmlsoap.org/ws/2005/04/discovery"
            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <SOAP-ENV:Body>
            <wsdd:ProbeMatches>
              <wsdd:ProbeMatch>
                <wsa:EndpointReference><wsa:Address>uuid:3fa1fe68-b915-4053-a3e1-c006c3afec0e</wsa:Address></wsa:EndpointReference>
                <wsdd:Types>dn:NetworkVideoTransmitter</wsdd:Types>
                <wsdd:Scopes>onvif://www.onvif.org/name/TP-IPC onvif://www.onvif.org/hardware/C200 onvif://www.onvif.org/Profile/Streaming</wsdd:Scopes>
                <wsdd:XAddrs>http://192.168.1.20:2020/onvif/device_service</wsdd:XAddrs>
                <wsdd:MetadataVersion>1</wsdd:MetadataVersion>
              </wsdd:ProbeMatch>
            </wsdd:ProbeMatches>
          </SOAP-ENV:Body>
        </SOAP-ENV:Envelope>
        """;

    [Fact]
    public void Parses_host_url_name_and_hardware()
    {
        var device = Assert.Single(WsDiscovery.ParseProbeMatches(TapoMatch));
        Assert.Equal("192.168.1.20", device.Host);
        Assert.Equal("http://192.168.1.20:2020/onvif/device_service", device.DeviceServiceUrl);
        Assert.Equal("TP-IPC", device.Name);
        Assert.Equal("C200", device.Hardware);
    }

    [Fact]
    public void Prefers_ipv4_address_and_unescapes_scopes()
    {
        var xml = TapoMatch
            .Replace("http://192.168.1.20:2020/onvif/device_service",
                     "http://[fe80::1]/onvif/device_service http://192.168.1.30/onvif/device_service")
            .Replace("name/TP-IPC", "name/IPC%20Imou");
        var device = Assert.Single(WsDiscovery.ParseProbeMatches(xml));
        Assert.Equal("192.168.1.30", device.Host);
        Assert.Equal("IPC Imou", device.Name);
    }

    [Fact]
    public void Garbage_returns_empty() =>
        Assert.Empty(WsDiscovery.ParseProbeMatches("not xml at all"));

    [Fact]
    public void Probe_is_valid_xml_with_message_id_and_type()
    {
        var id = Guid.NewGuid();
        var probe = WsDiscovery.BuildProbe(id);
        XDocument.Parse(probe);
        Assert.Contains($"uuid:{id}", probe);
        Assert.Contains("dn:NetworkVideoTransmitter", probe);
    }

    [Fact]
    public async Task ProbeAsync_completes_within_timeout()
    {
        var started = DateTime.UtcNow;
        await WsDiscovery.ProbeAsync(TimeSpan.FromMilliseconds(300));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }
}
