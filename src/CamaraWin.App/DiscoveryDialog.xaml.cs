using System.Windows;
using CamaraWin.Core;
using CamaraWin.Core.Onvif;

namespace CamaraWin.App;

public partial class DiscoveryDialog : Window
{
    readonly HashSet<string> _knownHosts;
    bool _closed;

    public DiscoveryDialog(IEnumerable<string> knownHosts)
    {
        InitializeComponent();
        _knownHosts = new HashSet<string>(knownHosts, StringComparer.OrdinalIgnoreCase);
        Loaded += async (_, _) => await SearchAsync();
        Closed += (_, _) => _closed = true;
    }

    public Camera? Result { get; private set; }

    public sealed record Row(DiscoveredDevice Device, string Host, string Name, string Hardware, string Status);

    async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    async Task SearchAsync()
    {
        SearchButton.IsEnabled = false;
        StatusText.Text = "Buscando cámaras ONVIF en la red…";
        try
        {
            var devices = await WsDiscovery.ProbeAsync(TimeSpan.FromSeconds(3));
            DeviceList.ItemsSource = devices
                .Select(d => new Row(d, d.Host, d.Name ?? "", d.Hardware ?? "", _knownHosts.Contains(d.Host) ? "Ya añadida" : ""))
                .ToList();
            StatusText.Text = devices.Count == 0
                ? "No se encontró ninguna. Puede que no tengan ONVIF activado: añádelas a mano."
                : $"{devices.Count} dispositivo(s) encontrados.";
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
