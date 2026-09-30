using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Discovery;

namespace Centinela.Desktop.Tests;

public class NetworkListTests
{
    static DiscoveredDevice Device(string host) => new(host, $"http://{host}/onvif/device_service", null, null);

    [Fact]
    public void Constructor_lists_the_pc_the_cameras_and_the_saved_networks_in_order()
    {
        var list = new NetworkList(["192.168.1.0/24"], ["10.20.30.118"], ["10.40.40.0/24"]);

        Assert.Equal(["192.168.1.0/24 — red de este PC", "10.20.30.0/24 — tus cámaras", "10.40.40.0/24 — guardada"],
            list.Items.Select(i => i.Label));
        Assert.All(list.Items, i => Assert.True(i.IsChecked));
        Assert.Equal([false, true, true], list.Items.Select(i => i.CanUncheck));
        Assert.Equal([false, false, true], list.Items.Select(i => i.Saved));
        Assert.True(list.IsLocal("192.168.1.0/24"));
        Assert.False(list.IsLocal("10.20.30.0/24"));
    }

    [Fact]
    public void A_network_of_the_pc_and_of_a_camera_appears_once_and_cannot_be_unticked()
    {
        var list = new NetworkList(["192.168.1.0/24"], ["192.168.1.50"], []);

        var item = Assert.Single(list.Items);
        Assert.Equal("192.168.1.0/24 — red de este PC", item.Label);
        Assert.False(item.CanUncheck);
    }

    [Fact]
    public void Adding_a_listed_network_only_ticks_it()
    {
        var list = new NetworkList([], [], ["10.40.40.0/24"]);
        list.Items[0].IsChecked = false;

        var again = list.Add("10.40.40.0/24".ToUpperInvariant(), "añadida a mano", isChecked: true, canUncheck: true, saved: true);

        Assert.Same(list.Items[0], again);
        Assert.Single(list.Items);
        Assert.True(again.IsChecked);
        Assert.Equal("10.40.40.0/24 — guardada", again.Label);
        list.Add("10.40.40.0/24", "", isChecked: false, canUncheck: true, saved: true);
        Assert.True(again.IsChecked);
    }

    [Fact]
    public void Saved_networks_are_the_ticked_saveable_ones()
    {
        var list = new NetworkList(["192.168.1.0/24"], ["10.20.30.118"], ["10.40.40.0/24", "10.50.50.0/24"]);
        list.Items[3].IsChecked = false;
        list.Add("10.60.60.1-50", "añadida a mano", isChecked: true, canUncheck: true, saved: true);

        Assert.Equal(["10.40.40.0/24", "10.60.60.1-50"], list.SavedNetworks);
    }

    [Fact]
    public void Targets_are_the_ticked_networks_not_of_this_pc()
    {
        var list = new NetworkList(["192.168.1.0/24"], ["10.20.30.118"], ["10.40.40.0/24", "10.50.50.0/24"]);
        list.Items[3].IsChecked = false;

        Assert.Equal("10.20.30.0/24,10.40.40.0/24", list.TargetsText);
        list.CheckAll();
        Assert.Equal("10.20.30.0/24,10.40.40.0/24,10.50.50.0/24", list.TargetsText);
    }

    [Fact]
    public void Note_change_announces_the_label()
    {
        var list = new NetworkList([], [], ["10.40.40.0/24"]);
        var changed = new List<string?>();
        list.Items[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        list.Items[0].Note = "sin cámaras ONVIF";

        Assert.Contains(nameof(NetworkItem.Label), changed);
        Assert.Equal("10.40.40.0/24 — sin cámaras ONVIF", list.Items[0].Label);
    }

    [Fact]
    public void Detection_adds_the_networks_with_cameras_and_the_empty_ones_unticked()
    {
        var known = new[] { "10.40.40.7" };
        var list = new NetworkList(["192.168.1.0/24"], known, []);

        var (withCameras, empty) = list.ApplyDetection(
            [Device("10.20.20.5"), Device("10.20.20.6")],
            ["192.168.1.0/24", "10.20.20.0/24", "10.30.30.0/24", "10.40.40.0/24"],
            known);

        Assert.Equal((1, 2), (withCameras, empty));
        Assert.Equal(["192.168.1.0/24 — red de este PC", "10.40.40.0/24 — tus cámaras",
            "10.20.20.0/24 — detectada: 2 cámara(s)", "10.30.30.0/24 — sin cámaras ONVIF"], list.Items.Select(i => i.Label));
        Assert.All(list.Items.Skip(2), i => Assert.False(i.IsChecked));
        Assert.True(list.Items[1].IsChecked); // already listed: detection never unticks it
        Assert.All(list.Items.Skip(2), i => Assert.True(i.Saved && i.CanUncheck));
    }
}
