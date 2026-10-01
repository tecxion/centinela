using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using Centinela.Core.Onvif;

namespace Centinela.Core.Tests;

public class NetworkScanTests
{
    static readonly IPAddress[] Local = [IPAddress.Parse("10.0.7.20")];

    [Fact]
    public async Task Networks_whose_router_answers_are_listed_sorted()
    {
        var result = await NetworkScan.FindNetworksAsync(Local,
            (target, _) => Task.FromResult(target.ToString() is "10.0.9.1" or "10.0.3.254"));
        Assert.Equal(["10.0.3.0/24", "10.0.9.0/24"], result.Networks);
        Assert.False(result.PingUnavailable);
    }

    [Fact]
    public async Task An_unreachable_router_is_just_not_a_network()
    {
        var result = await NetworkScan.FindNetworksAsync(Local, (target, _) =>
            target.ToString() == "10.0.4.1" ? Task.FromResult(true) : throw new PingException("unreachable"));
        Assert.Equal(["10.0.4.0/24"], result.Networks);
        Assert.False(result.PingUnavailable);
    }

    public static TheoryData<Exception> NotAllowed() => new()
    {
        new PlatformNotSupportedException(),
        new UnauthorizedAccessException(),
        new Win32Exception(13),
        new InvalidOperationException(),
        // Ping wraps most failures (the ping utility missing, no permission) in a PingException.
        new PingException("ping", new Win32Exception(2)),
        new PingException("ping", new UnauthorizedAccessException()),
    };

    [Theory]
    [MemberData(nameof(NotAllowed))]
    public async Task Ping_not_allowed_keeps_the_networks_found_so_far_and_says_so(Exception error)
    {
        var calls = 0;
        var result = await NetworkScan.FindNetworksAsync(Local, (target, _) =>
        {
            calls++;
            var text = target.ToString();
            if (text == "10.0.2.1") return Task.FromResult(true);
            if (text == "10.0.5.1") throw error;
            return Task.FromResult(false);
        });
        Assert.True(result.PingUnavailable);
        Assert.Equal(["10.0.2.0/24"], result.Networks);
        // No more pings once the system refused one (.0.1 .0.254 … .5.1 = 11 calls).
        Assert.Equal(11, calls);
    }

    [Fact]
    public async Task Cancelling_still_throws()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkScan.FindNetworksAsync(Local, (_, _) => Task.FromResult(false), cancel.Token));
    }
}
