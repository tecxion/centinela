using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows;
using Centinela.Core;
using Centinela.Core.Onvif;

namespace Centinela.App;

/// <summary>
/// «Buscar en red»: ONVIF search on this PC's network (multicast) plus, one address at a time, on the ticked
/// networks: those of the cameras already added, networks saved from earlier searches, typed ones and those
/// found by «Detectar otras redes» (a paced sweep of the PC's /16). Cameras already added are listed too.
/// </summary>
public partial class DiscoveryDialog : Window
{
    readonly HashSet<string> _knownHosts;
    readonly HashSet<string> _localNetworks;
    readonly ObservableCollection<NetworkItem> _networks = [];
    readonly Dictionary<string, DiscoveredDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? _detect;
    bool _closed;

    public sealed class NetworkItem(string network, string note, bool isChecked, bool canUncheck, bool saved) : INotifyPropertyChanged
    {
        string _note = note;
        bool _isChecked = isChecked;

        public string Network { get; } = network;
        public bool CanUncheck { get; } = canUncheck;
        /// <summary>Remembered for the next search when ticked (the PC's and the cameras' networks are derived again).</summary>
        public bool Saved { get; } = saved;
        public string Label => $"{Network} — {_note}";

        public string Note
        {
            set { _note = value; PropertyChanged?.Invoke(this, new(nameof(Label))); }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <param name="savedNetworks">Networks ticked in earlier searches (settings).</param>
    public DiscoveryDialog(IEnumerable<string> knownHosts, IEnumerable<string> savedNetworks)
    {
        InitializeComponent();
        _knownHosts = new HashSet<string>(knownHosts, StringComparer.OrdinalIgnoreCase);
        _localNetworks = new HashSet<string>(ProbeTargets.NetworksOf(WsDiscovery.LocalAddresses().Select(a => a.ToString())));
        foreach (var network in _localNetworks) AddNetwork(network, "red de este PC", isChecked: true, canUncheck: false, saved: false);
        foreach (var network in ProbeTargets.NetworksOf(_knownHosts)) AddNetwork(network, "tus cámaras", isChecked: true, canUncheck: true, saved: false);
        foreach (var network in savedNetworks) AddNetwork(network, "guardada", isChecked: true, canUncheck: true, saved: true);
        NetworksList.ItemsSource = _networks;
        Loaded += async (_, _) => await SearchAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _detect?.Cancel();
        };
    }

    public Camera? Result { get; private set; }

    /// <summary>Ticked networks to remember for the next search (not this PC's, not the cameras' own).</summary>
    public IReadOnlyList<string> SavedNetworks =>
        _networks.Where(n => n.IsChecked && n.Saved).Select(n => n.Network).ToList();

    public sealed record Row(DiscoveredDevice Device, string Host, string Name, string Hardware, string Status);

    /// <summary>Adds the network unless listed already (then it is only ticked when asked to be).</summary>
    NetworkItem AddNetwork(string network, string note, bool isChecked, bool canUncheck, bool saved)
    {
        if (_networks.FirstOrDefault(n => string.Equals(n.Network, network, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            if (isChecked) existing.IsChecked = true;
            return existing;
        }
        var item = new NetworkItem(network, note, isChecked, canUncheck, saved);
        _networks.Add(item);
        return item;
    }

    async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    async Task SearchAsync()
    {
        IReadOnlyList<IPAddress> targets;
        try
        {
            // This PC's own network is covered by the multicast probe.
            targets = ProbeTargets.Parse(string.Join(",", _networks
                .Where(n => n.IsChecked && !_localNetworks.Contains(n.Network)).Select(n => n.Network)));
        }
        catch (FormatException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }
        SearchButton.IsEnabled = false;
        StatusText.Text = "Buscando cámaras ONVIF en la red…";
        try
        {
            _devices.Clear();
            ShowDevices(await WsDiscovery.ProbeAsync(TimeSpan.FromSeconds(3), targets));
            StatusText.Text = _devices.Count == 0
                ? "No se encontró ninguna. Si tus cámaras están en otra red, pulsa «Detectar otras redes»; si no, puede que no tengan ONVIF activado: añádelas a mano."
                : Summary();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error al buscar: {ex.Message}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    void ShowDevices(IEnumerable<DiscoveredDevice> devices)
    {
        foreach (var device in devices) _devices[device.Host] = device;
        DeviceList.ItemsSource = _devices.Values
            .OrderBy(d => IPAddress.TryParse(d.Host, out var ip) ? (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ip.GetAddressBytes())) : uint.MaxValue)
            .Select(d => new Row(d, d.Host, d.Name ?? "", d.Hardware ?? "", _knownHosts.Contains(d.Host) ? "Ya añadida" : ""))
            .ToList();
    }

    string Summary() =>
        $"{_devices.Count} cámara(s) encontradas, {_devices.Keys.Count(_knownHosts.Contains)} ya añadida(s).";

    void AddNetwork_Click(object sender, RoutedEventArgs e)
    {
        var text = NewNetworkBox.Text.Trim();
        if (text.Length == 0) return;
        try
        {
            if (ProbeTargets.Parse(text).Count == 0) return;
        }
        catch (FormatException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }
        AddNetwork(text, "añadida a mano", isChecked: true, canUncheck: true, saved: true);
        NewNetworkBox.Clear();
        StatusText.Text = "Red añadida: pulsa «Buscar».";
    }

    void CheckAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var network in _networks) network.IsChecked = true;
    }

    /// <summary>
    /// Sweeps every address of the /16 around this PC (not its own network, already searched) and lists the
    /// networks where a camera answered, unticked, with the cameras it found.
    /// </summary>
    async void Detect_Click(object sender, RoutedEventArgs e)
    {
        if (_detect is not null)
        {
            _detect.Cancel(); // the button reads «Detener» meanwhile
            return;
        }
        var zone = WsDiscovery.LocalAddresses()
            .SelectMany(ProbeTargets.Zone)
            .Where(a => !_localNetworks.Contains(ProbeTargets.NetworkOf(a.ToString()) ?? ""))
            .DistinctBy(a => a.ToString())
            .ToList();
        if (zone.Count == 0)
        {
            StatusText.Text = "Este PC no está conectado a ninguna red.";
            return;
        }
        var detect = _detect = new CancellationTokenSource();
        DetectButton.Content = "⏹ Detener";
        SearchButton.IsEnabled = false;
        DetectProgress.Value = 0;
        DetectProgress.Visibility = Visibility.Visible;
        StatusText.Text = $"Detectando redes: preguntando a {zone.Count:N0} direcciones (unos 20–30 s)…";
        try
        {
            var found = await WsDiscovery.SweepAsync(zone, new Progress<double>(p => DetectProgress.Value = p), detect.Token);
            if (_closed) return;
            var groups = found.GroupBy(d => ProbeTargets.NetworkOf(d.Host)).Where(g => g.Key is not null).ToList();
            foreach (var group in groups)
            {
                var item = AddNetwork(group.Key!, "", isChecked: false, canUncheck: true, saved: true);
                item.Note = $"detectada: {group.Count()} cámara(s)";
            }
            ShowDevices(found);
            StatusText.Text = groups.Count == 0
                ? "No se encontraron cámaras en otras redes."
                : $"Redes con cámaras: {string.Join(", ", groups.Select(g => g.Key))}. Márcalas para incluirlas en las próximas búsquedas. {Summary()}";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) StatusText.Text = "Detección cancelada.";
        }
        finally
        {
            _detect = null;
            detect.Dispose();
            if (!_closed)
            {
                DetectButton.Content = "📡 Detectar otras redes";
                SearchButton.IsEnabled = true;
                DetectProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not Row row)
        {
            StatusText.Text = "Selecciona una cámara de la lista.";
            return;
        }
        AddButton.IsEnabled = false;
        StatusText.Text = "Consultando la cámara…";
        try
        {
            var user = UserBox.Text.Trim();
            var password = PasswordInput.Password;
            var deviceUrl = new Uri(row.Device.DeviceServiceUrl);
            using var http = OnvifClient.CreateHttpClient(deviceUrl, user, password);
            var client = new OnvifClient(http, deviceUrl, user, password);

            // Optional: without device info the camera is still added (timeouts, HTTP or XML errors).
            OnvifDeviceInfo? info = null;
            try { info = await client.GetDeviceInformationAsync(); }
            catch (Exception ex) when (ex is not OnvifAuthException) { }

            var (main, sub) = await client.ResolveStreamUrisAsync();
            if (_closed) return;
            Result = new Camera
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
            DialogResult = true;
        }
        catch (OnvifAuthException)
        {
            if (_closed) return;
            StatusText.Text = "Usuario o contraseña incorrectos.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            StatusText.Text = $"No se pudo consultar la cámara: {ex.Message}";
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    static string DisplayName(OnvifDeviceInfo? info, Row row) =>
        info?.Model is { Length: > 0 } model ? $"{model} ({row.Host})"
        : row.Name.Length > 0 ? $"{row.Name} ({row.Host})"
        : row.Host;
}
