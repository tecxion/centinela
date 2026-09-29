using Centinela.Core.Onvif;

namespace Centinela.Core.Tests;

public class ProbeTargetsTests
{
    static string[] Parse(string text) => ProbeTargets.Parse(text).Select(a => a.ToString()).ToArray();

    [Fact]
    public void A_24_network_gives_its_254_hosts()
    {
        var hosts = Parse("10.20.30.0/24");
        Assert.Equal(254, hosts.Length);
        Assert.Equal("10.20.30.1", hosts[0]);
        Assert.Equal("10.20.30.254", hosts[^1]);
    }

    [Fact]
    public void Star_ranges_single_addresses_and_duplicates()
    {
        Assert.Equal(254, Parse("192.168.1.*").Length);
        Assert.Equal(["10.0.0.5", "10.0.0.6", "10.0.0.7"], Parse("10.0.0.5-7"));
        Assert.Equal(["10.0.0.5", "10.0.0.6"], Parse("10.0.0.5-10.0.0.6"));
        Assert.Equal(["10.20.30.118", "10.0.0.1"], Parse("10.20.30.118, 10.0.0.1; 10.20.30.118"));
        Assert.Equal(["10.1.1.1"], Parse("10.1.1.1/32"));
        Assert.Empty(Parse("  "));
    }

    [Theory]
    [InlineData("10.20.30")]
    [InlineData("camara")]
    [InlineData("10.0.0.0/8")]
    [InlineData("10.0.0.9-3")]
    [InlineData("fe80::1")]
    public void Nonsense_or_too_big_is_a_format_error(string text) =>
        Assert.Throws<FormatException>(() => ProbeTargets.Parse(text));

    [Fact]
    public void Too_many_addresses_in_total_are_refused() =>
        Assert.Throws<FormatException>(() => ProbeTargets.Parse(
            string.Join(",", Enumerable.Range(0, 20).Select(i => $"10.0.{i}.0/24"))));

    [Fact]
    public void Networks_of_known_cameras_are_their_distinct_24s() =>
        Assert.Equal("10.20.30.0/24, 192.168.1.0/24",
            ProbeTargets.FromHosts(["10.20.30.118", "192.168.1.9", "10.20.30.104", "camara.local", ""]));
}
