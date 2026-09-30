using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Discovery;

namespace Centinela.Desktop.Views;

/// <summary>
/// «Buscar en red»: ONVIF search on this PC's network (multicast) plus, one address at a time, on the ticked
/// networks: those of the cameras already added, networks saved from earlier searches, typed ones and those
/// found by «Detectar otras redes» (a paced sweep of the PC's /16). Cameras already added are listed too.
/// </summary>
public partial class DiscoveryDialog : Window
{
    readonly HashSet<string> _knownHosts = new(StringComparer.OrdinalIgnoreCase);
    readonly NetworkList _networks = new([], [], []);
    readonly Dictionary<string, DiscoveredDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? _detect;
    bool _closed;

    public DiscoveryDialog() => InitializeComponent();

    /// <param name="savedNetworks">Networks ticked in earlier searches (settings).</param>
    public DiscoveryDialog(IEnumerable<string> knownHosts, IEnumerable<string> savedNetworks) : this()
    {
        _knownHosts.UnionWith(knownHosts);
        _networks = new NetworkList(ProbeTargets.NetworksOf(WsDiscovery.LocalAddresses().Select(a => a.ToString())),
            _knownHosts, savedNetworks);
        NetworksList.ItemsSource = _networks.Items;
        Opened += async (_, _) => await SearchAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _detect?.Cancel();
        };
    }

    public Camera? Result { get; private set; }

    /// <summary>Ticked networks to remember for the next search (not this PC's, not the cameras' own).</summary>
    public IReadOnlyList<string> SavedNetworks => _networks.SavedNetworks;

    async void Search_Click(object? sender, RoutedEventArgs e) => await SearchAsync();

    async Task SearchAsync()
    {
        IReadOnlyList<IPAddress> targets;
        try
        {
            // This PC's own network is covered by the multicast probe.
            targets = ProbeTargets.Parse(_networks.TargetsText);
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
        DeviceList.ItemsSource = DiscoveryResults.Rows(_devices.Values, _knownHosts);
    }

    string Summary() => DiscoveryResults.Summary(_devices.Count, _devices.Keys.Count(_knownHosts.Contains));

    void AddNetwork_Click(object? sender, RoutedEventArgs e)
    {
        var text = (NewNetworkBox.Text ?? "").Trim();
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
        _networks.Add(text, "añadida a mano", isChecked: true, canUncheck: true, saved: true);
        NewNetworkBox.Clear();
        StatusText.Text = "Red añadida: pulsa «Buscar».";
    }

    void CheckAll_Click(object? sender, RoutedEventArgs e) => _networks.CheckAll();

    /// <summary>
    /// Sweeps every address of the /16 around this PC (not its own network, already searched) and lists the
    /// networks where a camera answered, unticked, with the cameras it found.
    /// </summary>
    async void Detect_Click(object? sender, RoutedEventArgs e)
    {
        if (_detect is not null)
        {
            _detect.Cancel(); // the button reads «Detener» meanwhile
            return;
        }
        var zone = WsDiscovery.LocalAddresses()
            .SelectMany(ProbeTargets.Zone)
            .Where(a => !_networks.IsLocal(ProbeTargets.NetworkOf(a.ToString()) ?? ""))
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
        DetectProgress.IsVisible = true;
        StatusText.Text = $"Detectando redes: preguntando a {zone.Count:N0} direcciones (unos 20–30 s)…";
        try
        {
            // Networks that exist but have no ONVIF camera (servers, guests…) are found by pinging their router.
            var existing = NetworkScan.FindNetworksAsync(WsDiscovery.LocalAddresses(), detect.Token);
            var found = await WsDiscovery.SweepAsync(zone, new Progress<double>(p => DetectProgress.Value = p), detect.Token);
            var networks = await existing;
            if (_closed) return;
            var (withCameras, empty) = _networks.ApplyDetection(found, networks, _knownHosts);
            ShowDevices(found);
            StatusText.Text = withCameras == 0 && empty == 0
                ? "No se encontraron otras redes."
                : $"{withCameras} red(es) con cámaras y {empty} sin cámaras ONVIF. Marca las que quieras incluir en las próximas búsquedas. {Summary()}";
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
                DetectProgress.IsVisible = false;
            }
        }
    }

    async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DiscoveryRow row)
        {
            StatusText.Text = "Selecciona una cámara de la lista.";
            return;
        }
        AddButton.IsEnabled = false;
        StatusText.Text = "Consultando la cámara…";
        try
        {
            var user = (UserBox.Text ?? "").Trim();
            var password = PasswordInput.Text ?? "";
            var deviceUrl = new Uri(row.Device.DeviceServiceUrl);
            using var http = OnvifClient.CreateHttpClient(deviceUrl, user, password);
            var client = new OnvifClient(http, deviceUrl, user, password);

            // Optional: without device info the camera is still added (timeouts, HTTP or XML errors).
            OnvifDeviceInfo? info = null;
            try { info = await client.GetDeviceInformationAsync(); }
            catch (Exception ex) when (ex is not OnvifAuthException) { }

            var (main, sub) = await client.ResolveStreamUrisAsync();
            if (_closed) return;
            Result = DiscoveryResults.ToCamera(row, info, user, password, main, sub);
            Close(true);
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

    void Close_Click(object? sender, RoutedEventArgs e) => Close(false);
}
