using System.Net;
using Centinela.Core;
using Centinela.Core.Onvif;

namespace Centinela.Desktop.Services.Discovery;

/// <summary>A camera found by «Buscar en red», as its list shows it.</summary>
sealed record DiscoveryRow(DiscoveredDevice Device, string Host, string Name, string Hardware, string Status);

/// <summary>The cameras found by «Buscar en red»: their rows, the summary and the camera added from one.</summary>
static class DiscoveryResults
{
    /// <summary>Ordered by IP (names last); those already added say «Ya añadida».</summary>
    public static IReadOnlyList<DiscoveryRow> Rows(IEnumerable<DiscoveredDevice> devices, IReadOnlySet<string> knownHosts) =>
        devices
            .OrderBy(d => IPAddress.TryParse(d.Host, out var ip) ? (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ip.GetAddressBytes())) : uint.MaxValue)
            .Select(d => new DiscoveryRow(d, d.Host, d.Name ?? "", d.Hardware ?? "", knownHosts.Contains(d.Host) ? "Ya añadida" : ""))
            .ToList();

    public static string Summary(int found, int alreadyAdded) =>
        $"{found} cámara(s) encontradas, {alreadyAdded} ya añadida(s).";

    public static string DisplayName(OnvifDeviceInfo? info, DiscoveryRow row) =>
        info?.Model is { Length: > 0 } model ? $"{model} ({row.Host})"
        : row.Name.Length > 0 ? $"{row.Name} ({row.Host})"
        : row.Host;

    /// <summary>The camera to add: the ONVIF stream URLs as overrides, the port of the main one (or 554).</summary>
    public static Camera ToCamera(DiscoveryRow row, OnvifDeviceInfo? info, string user, string password, string main, string sub) => new()
    {
        Name = DisplayName(info, row),
        Brand = BrandInference.FromManufacturer(info?.Manufacturer ?? row.Device.Name),
        Host = row.Host,
        Port = Uri.TryCreate(main, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : 554,
        User = user,
        Password = password,
        MainUrlOverride = main,
        SubUrlOverride = sub,
    };
}
