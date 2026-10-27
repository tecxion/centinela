using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;

namespace Centinela.Core.Onvif;

/// <summary>The networks found and whether this system refused to ping (then only some were tried).</summary>
public sealed record NetworkScanResult(IReadOnlyList<string> Networks, bool PingUnavailable);

/// <summary>
/// Finds the /24 networks that exist around this PC even when no camera answers there: a ping to the usual
/// router addresses (.1 and .254) of each /24 in the PC's /16. About 512 pings, 64 at a time. Where the system does
/// not let Centinela ping (no permission, no ping utility), it stops and keeps what it found so far.
/// </summary>
public static class NetworkScan
{
    static readonly byte[] RouterHosts = [1, 254];
    static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(600);

    /// <summary>The <c>a.b.c.0/24</c> networks (sorted) whose .1 or .254 answered a ping.</summary>
    public static Task<NetworkScanResult> FindNetworksAsync(IEnumerable<IPAddress> locals, CancellationToken ct = default) =>
        FindNetworksAsync(locals, SystemPingAsync, OperatingSystem.IsWindows(), ct);

    /// <param name="ping">True when the address answered; throws as <see cref="Ping"/> does.</param>
    /// <param name="windows">On Windows a refusal is never assumed: every failure is that address not answering.</param>
    public static async Task<NetworkScanResult> FindNetworksAsync(IEnumerable<IPAddress> locals,
        Func<IPAddress, CancellationToken, Task<bool>> ping, bool windows, CancellationToken ct = default)
    {
        var targets = locals
            .Select(l => l.GetAddressBytes())
            .SelectMany(b => Enumerable.Range(0, 256).SelectMany(third =>
                RouterHosts.Select(host => new IPAddress([b[0], b[1], (byte)third, host]))))
            .DistinctBy(a => a.ToString())
            .ToList();
        var alive = new HashSet<string>();
        var unavailable = false;
        using var gate = new SemaphoreSlim(64);
        await Task.WhenAll(targets.Select(async target =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (Volatile.Read(ref unavailable)) return; // the system refused already: no more pings
                if (await ping(target, ct))
                    lock (alive) alive.Add(ProbeTargets.NetworkOf(target.ToString())!);
            }
            catch (Exception e) when (IsPingRefused(e, windows))
            {
                Volatile.Write(ref unavailable, true);
            }
            catch (PingException) { /* unreachable: not a network */ }
            finally
            {
                gate.Release();
            }
        }));
        var networks = ProbeTargets.NetworksOf(alive.Select(n => n.Replace(".0/24", ".1", StringComparison.Ordinal)));
        return new NetworkScanResult(networks, unavailable);
    }

    /// <summary>
    /// The system does not let this process ping (Linux, macOS): no permission for raw sockets or the ping utility
    /// (EPERM, EACCES), or no ping utility (ENOENT). <see cref="Ping"/> wraps these in a <see cref="PingException"/>.
    /// Anything else, a <see cref="System.Net.Sockets.SocketException"/> above all (no buffer space, host
    /// unreachable…), is one address not answering; on Windows always.
    /// </summary>
    public static bool IsPingRefused(Exception e, bool windows)
    {
        if (windows) return false;
        if (e is PingException ping)
        {
            if (ping.InnerException is not { } inner) return false;
            e = inner;
        }
        return e is PlatformNotSupportedException or UnauthorizedAccessException
            || e is Win32Exception { NativeErrorCode: EPERM or ENOENT or EACCES } and not System.Net.Sockets.SocketException;
    }

    const int EPERM = 1;
    const int ENOENT = 2;
    const int EACCES = 13;

    static async Task<bool> SystemPingAsync(IPAddress target, CancellationToken ct)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(target, PingTimeout, cancellationToken: ct);
        return reply.Status == IPStatus.Success;
    }
}
