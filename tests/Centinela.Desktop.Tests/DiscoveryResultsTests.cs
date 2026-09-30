using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Discovery;

namespace Centinela.Desktop.Tests;

public class DiscoveryResultsTests
{
    static DiscoveredDevice Device(string host, string? name = null, string? hardware = null) =>
        new(host, $"http://{host}/onvif/device_service", name, hardware);

    static DiscoveryRow Row(string host, string name = "") => new(Device(host), host, name, "", "");

    [Fact]
    public void Rows_are_ordered_by_ip_with_names_last()
    {
        var rows = DiscoveryResults.Rows(
            [Device("camara.local"), Device("192.168.1.100", "C200", "Tapo"), Device("192.168.1.20")],
            new HashSet<string>());

        Assert.Equal(["192.168.1.20", "192.168.1.100", "camara.local"], rows.Select(r => r.Host));
        Assert.Equal("C200", rows[1].Name);
        Assert.Equal("Tapo", rows[1].Hardware);
        Assert.Equal("", rows[0].Name);
        Assert.Equal("", rows[0].Hardware);
    }

    [Fact]
    public void Cameras_already_added_are_marked_ignoring_case()
    {
        var rows = DiscoveryResults.Rows([Device("Camara.Local"), Device("10.0.0.5")],
            new HashSet<string>(["camara.local"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal("", rows[0].Status);
        Assert.Equal("Ya añadida", rows[1].Status);
    }

    [Fact]
    public void Summary_counts_found_and_already_added() =>
        Assert.Equal("3 cámara(s) encontradas, 1 ya añadida(s).", DiscoveryResults.Summary(3, 1));

    [Fact]
    public void Display_name_prefers_the_model_then_the_name_then_the_host()
    {
        Assert.Equal("C200 (10.0.0.5)", DiscoveryResults.DisplayName(new OnvifDeviceInfo("TP-Link", "C200"), Row("10.0.0.5", "Tapo")));
        Assert.Equal("Tapo (10.0.0.5)", DiscoveryResults.DisplayName(new OnvifDeviceInfo("TP-Link", ""), Row("10.0.0.5", "Tapo")));
        Assert.Equal("Tapo (10.0.0.5)", DiscoveryResults.DisplayName(null, Row("10.0.0.5", "Tapo")));
        Assert.Equal("10.0.0.5", DiscoveryResults.DisplayName(null, Row("10.0.0.5")));
    }

    [Fact]
    public void Camera_takes_the_port_of_the_main_url_or_554()
    {
        var withPort = DiscoveryResults.ToCamera(Row("10.0.0.5"), new OnvifDeviceInfo("TP-Link", "C200"),
            "admin", "secreto", "rtsp://h:8554/x", "rtsp://h:8554/y");

        Assert.Equal(8554, withPort.Port);
        Assert.Equal("10.0.0.5", withPort.Host);
        Assert.Equal("C200 (10.0.0.5)", withPort.Name);
        Assert.Equal("admin", withPort.User);
        Assert.Equal("secreto", withPort.Password);
        Assert.Equal("rtsp://h:8554/x", withPort.MainUrlOverride);
        Assert.Equal("rtsp://h:8554/y", withPort.SubUrlOverride);
        Assert.Equal(BrandInference.FromManufacturer("TP-Link"), withPort.Brand);

        var without = DiscoveryResults.ToCamera(Row("10.0.0.5", "IPC"), null, "", "", "rtsp://h/x", "rtsp://h/y");
        Assert.Equal(554, without.Port);
        Assert.Equal(BrandInference.FromManufacturer("IPC"), without.Brand);
    }
}
