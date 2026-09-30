using System.Collections.ObjectModel;
using Centinela.Core.Onvif;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.Services.Discovery;

/// <summary>A network in «Redes donde buscar»: its check box and note.</summary>
sealed partial class NetworkItem(string network, string note, bool isChecked, bool canUncheck, bool saved) : ObservableObject
{
    [ObservableProperty] private bool _isChecked = isChecked;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private string _note = note;

    public string Network { get; } = network;
    public bool CanUncheck { get; } = canUncheck;
    /// <summary>Remembered for the next search when ticked (the PC's and the cameras' networks are derived again).</summary>
    public bool Saved { get; } = saved;
    public string Label => $"{Network} — {Note}";
}

/// <summary>
/// «Redes donde buscar»: this PC's networks (always searched), the cameras' networks, networks saved from earlier
/// searches, typed ones and those found by «Detectar otras redes».
/// </summary>
sealed class NetworkList
{
    readonly HashSet<string> _local;

    /// <param name="savedNetworks">Networks ticked in earlier searches (settings).</param>
    public NetworkList(IEnumerable<string> localNetworks, IEnumerable<string> knownHosts, IEnumerable<string> savedNetworks)
    {
        _local = new HashSet<string>(localNetworks);
        foreach (var network in _local) Add(network, "red de este PC", isChecked: true, canUncheck: false, saved: false);
        foreach (var network in ProbeTargets.NetworksOf(knownHosts)) Add(network, "tus cámaras", isChecked: true, canUncheck: true, saved: false);
        foreach (var network in savedNetworks) Add(network, "guardada", isChecked: true, canUncheck: true, saved: true);
    }

    public ObservableCollection<NetworkItem> Items { get; } = [];

    /// <summary>Ticked networks to remember for the next search (not this PC's, not the cameras' own).</summary>
    public IReadOnlyList<string> SavedNetworks => Items.Where(n => n.IsChecked && n.Saved).Select(n => n.Network).ToList();

    /// <summary>The ticked networks for <see cref="ProbeTargets.Parse"/>; this PC's own is covered by the multicast probe.</summary>
    public string TargetsText => string.Join(",", Items.Where(n => n.IsChecked && !IsLocal(n.Network)).Select(n => n.Network));

    public bool IsLocal(string network) => _local.Contains(network);

    /// <summary>Adds the network unless listed already (then it is only ticked when asked to be).</summary>
    public NetworkItem Add(string network, string note, bool isChecked, bool canUncheck, bool saved)
    {
        if (Items.FirstOrDefault(n => string.Equals(n.Network, network, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            if (isChecked) existing.IsChecked = true;
            return existing;
        }
        var item = new NetworkItem(network, note, isChecked, canUncheck, saved);
        Items.Add(item);
        return item;
    }

    public void CheckAll()
    {
        foreach (var network in Items) network.IsChecked = true;
    }

    /// <summary>
    /// «Detectar otras redes»: lists, unticked, the networks where a camera answered and the other existing networks
    /// (not this PC's); returns how many of each.
    /// </summary>
    public (int WithCameras, int Empty) ApplyDetection(IReadOnlyList<DiscoveredDevice> found,
        IReadOnlyList<string> existingNetworks, IEnumerable<string> knownHosts)
    {
        var groups = found.GroupBy(d => ProbeTargets.NetworkOf(d.Host)).Where(g => g.Key is not null).ToList();
        foreach (var group in groups)
        {
            var item = Add(group.Key!, "", isChecked: false, canUncheck: true, saved: true);
            item.Note = $"detectada: {group.Count()} cámara(s)";
        }
        var hosts = knownHosts.ToList();
        var empty = existingNetworks.Where(n => !IsLocal(n) && groups.All(g => g.Key != n)).ToList();
        foreach (var network in empty)
        {
            var item = Add(network, "", isChecked: false, canUncheck: true, saved: true);
            if (!hosts.Any(h => ProbeTargets.NetworkOf(h) == network)) item.Note = "sin cámaras ONVIF";
        }
        return (groups.Count, empty.Count);
    }
}
