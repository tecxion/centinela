using System.Net;
using System.Net.NetworkInformation;

namespace Centinela.Core.Onvif;

/// <summary>
/// Finds the /24 networks that exist around this PC even when no camera answers there: a ping to the usual
/// router addresses (.1 and .254) of each /24 in the PC's /16. About 512 pings, 64 at a time.
/// </summary>
public static class NetworkScan
{
    static readonly byte[] RouterHosts = [1, 254];

    /// <summary>The <c>a.b.c.0/24</c> networks (sorted) whose .1 or .254 answered a ping.</summary>
    public static async Task<IReadOnlyList<string>> FindNetworksAsync(IEnumerable<IPAddress> locals, CancellationToken ct = default)
    {
        var targets = locals
            .Select(l => l.GetAddressBytes())
            .SelectMany(b => Enumerable.Range(0, 256).SelectMany(third =>
                RouterHosts.Select(host => new IPAddress([b[0], b[1], (byte)third, host]))))
            .DistinctBy(a => a.ToString())
            .ToList();
        var alive = new HashSet<string>();
        using var gate = new SemaphoreSlim(64);
        await Task.WhenAll(targets.Select(async target =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(600), cancellationToken: ct);
                if (reply.Status == IPStatus.Success)
                    lock (alive) alive.Add(ProbeTargets.NetworkOf(target.ToString())!);
            }
            catch (PingException) { /* unreachable: not a network */ }
            finally
            {
                gate.Release();
            }
        }));
        return ProbeTargets.NetworksOf(alive.Select(n => n.Replace(".0/24", ".1", StringComparison.Ordinal)));
    }
}
