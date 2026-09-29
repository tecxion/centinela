using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Media;

namespace Centinela.App;

/// <summary>
/// «Calidad de la cámara…»: reports the camera's encoder settings and what it accepts (ONVIF and the Dahua/Imou
/// HTTP API) and lets the user change resolution, fps and bitrate through ONVIF. Nothing is written without
/// «Aplicar…» and its confirmation. The values found before Centinela's first change are kept on the camera for
/// «Valores originales». The report never contains the password.
/// </summary>
public partial class CameraQualityWindow : Window
{
    static readonly TimeSpan OnvifTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(8);
    static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    const int MinBitrate = 32;
    const int MaxBitrate = 16384;

    readonly Camera _camera;
    readonly Action _save;
    readonly List<EditorRow> _rows = [];
    CancellationTokenSource? _query;
    string _log = ""; // results of the last «Aplicar», shown above the report

    sealed record EditorRow(string Token, EncoderSettings Current, EncoderOptions? Options,
        ComboBox Resolution, TextBox Fps, TextBox Bitrate);

    /// <param name="camera">The live camera (its <see cref="Camera.OriginalEncoders"/> may be set here).</param>
    /// <param name="save">Saves the camera list after <see cref="Camera.OriginalEncoders"/> was set.</param>
    public CameraQualityWindow(Camera camera, Action save)
    {
        InitializeComponent();
        _camera = camera;
        _save = save;
        Title = $"Calidad de la cámara — {camera.Name}";
        Loaded += (_, _) => _ = QueryAsync(includeApi: true);
        Closed += (_, _) => _query?.Cancel();
    }

    string Host => HostOf(_camera);
    Uri BaseUrl => new($"http://{Host}/");
    Uri DeviceUrl => new(BaseUrl, "/onvif/device_service");

    // Generous: a camera on weak Wi-Fi may take seconds per answer, and each authenticated call is two round
    // trips (Digest challenge, then the request again).
    HttpClient CreateOnvifHttp() => OnvifClient.CreateHttpClient(DeviceUrl, _camera.User, _camera.Password, OnvifTimeout);

    async Task QueryAsync(bool includeApi)
    {
        _query?.Cancel();
        var query = _query = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        SetBusy(true);
        var report = new StringBuilder(_log);
        void Line(string text = "")
        {
            report.AppendLine(text);
            if (!query.IsCancellationRequested) Report.Text = report.ToString();
        }

        Line($"Cámara: {_camera.Name} ({_camera.Brand}) · {Host}");
        Line($"Usuario: {(_camera.User.Length > 0 ? _camera.User : "(ninguno)")}");
        Line();
        if (Host.Length == 0)
        {
            Line("Esta cámara no tiene dirección de red: no se puede consultar.");
            SetBusy(false);
            return;
        }

        using var http = CreateOnvifHttp();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string Took() => string.Create(Spanish, $"{watch.Elapsed.TotalSeconds:0.0} s");

        Line("── ONVIF ──");
        IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)> configurations = [];
        try
        {
            var client = new OnvifClient(http, DeviceUrl, _camera.User, _camera.Password);
            watch.Restart();
            try
            {
                var info = await client.GetDeviceInformationAsync(query.Token);
                Line($"Dispositivo: {info.Manufacturer} {info.Model}  ({Took()})");
            }
            catch (OnvifException ex) when (ex is not OnvifAuthException)
            {
                Line($"Dispositivo: no responde ({ex.Message})");
            }
            watch.Restart();
            configurations = await client.GetEncoderSettingsAsync(query.Token);
            Line($"Ajustes de vídeo leídos en {Took()}:");
            if (configurations.Count == 0) Line("No informa de ninguna configuración de vídeo.");
            foreach (var (token, settings, options) in configurations)
            {
                Line(Describe(settings) + $"  [token {token}]");
                if (options is null) Line("    Admite: (no lo informa)");
                else
                {
                    Line($"    Admite fps {Range(options.FpsMin, options.FpsMax)} · bitrate {Range(options.BitrateMinKbps, options.BitrateMaxKbps)} kbps");
                    if (options.Resolutions.Count > 0)
                        Line("    Resoluciones: " + string.Join(", ", options.Resolutions.Select(r => $"{r.Width}×{r.Height}")));
                }
            }
            if (EncoderSnapshot.Parse(_camera.OriginalEncoders) is { Count: > 0 } originals)
                Line("Originales (antes de cambiarla con Centinela): " + string.Join(" · ",
                    originals.Select(o => $"{o.Key} {o.Value.Width}×{o.Value.Height} {o.Value.Fps} fps {o.Value.BitrateKbps} kbps")));
        }
        catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Line($"No se pudo consultar tras {Took()}: {Explain(ex)}");
        }
        BuildEditors(configurations);
        Line();

        if (includeApi)
        {
            Line("── API Dahua/Imou ──");
            using var api = OnvifClient.CreateHttpClient(DeviceUrl, _camera.User, _camera.Password, ApiTimeout);
            try
            {
                var streams = await DahuaApi.GetEncodeAsync(api, BaseUrl, query.Token);
                if (streams.Count == 0) Line("Responde, pero sin ajustes de vídeo reconocibles.");
                foreach (var stream in streams) Line(Describe(stream));
                try
                {
                    var caps = await DahuaApi.GetEncodeCapsAsync(api, BaseUrl, query.Token);
                    if (caps.Count > 0)
                    {
                        Line("Capacidades:");
                        foreach (var cap in caps.Take(60)) Line("    " + cap);
                    }
                }
                catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException)
                {
                    Line($"Capacidades: no las informa ({Explain(ex)})");
                }
            }
            catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException)
            {
                Line($"No se pudo consultar: {Explain(ex)}");
                Line("(Muchas Imou traen esta API desactivada; basta con que responda ONVIF.)");
            }
        }
        SetBusy(false);
    }

    void SetBusy(bool busy)
    {
        RetryButton.IsEnabled = !busy;
        EditBox.IsEnabled = !busy && _rows.Count > 0;
        RestoreButton.IsEnabled = EncoderSnapshot.Parse(_camera.OriginalEncoders).Count > 0;
    }

    /// <summary>One row per ONVIF encoder configuration, pre-filled with its current values.</summary>
    void BuildEditors(IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)> configurations)
    {
        _rows.Clear();
        Editors.Children.Clear();
        Editors.RowDefinitions.Clear();
        Editors.ColumnDefinitions.Clear();
        foreach (var width in new[] { 150.0, 140, 70, 110 })
            Editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        Editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void Put(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Editors.Children.Add(element);
        }
        Editors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headers = ["Flujo", "Resolución", "fps", "Bitrate (kbps)", "Ahora"];
        for (var c = 0; c < headers.Length; c++)
            Put(new TextBlock { Text = headers[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 4) }, 0, c);

        foreach (var (token, settings, options) in configurations.OrderByDescending(c => c.Settings.Width * c.Settings.Height))
        {
            var row = Editors.RowDefinitions.Count;
            Editors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var resolutions = (options?.Resolutions ?? []).ToList();
            if (!resolutions.Contains((settings.Width, settings.Height))) resolutions.Insert(0, (settings.Width, settings.Height));
            var resolution = new ComboBox { Margin = new Thickness(0, 2, 8, 2) };
            foreach (var r in resolutions) resolution.Items.Add(new ComboBoxItem { Content = $"{r.Width}×{r.Height}", Tag = r });
            var fps = new TextBox { Margin = new Thickness(0, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
            var bitrate = new TextBox { Margin = new Thickness(0, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
            var editor = new EditorRow(token, settings, options, resolution, fps, bitrate);
            Fill(editor, new EncoderChange(settings.Width, settings.Height, settings.Fps, settings.BitrateKbps));

            var role = row == 1 ? "Principal" : "Secundario";
            Put(new TextBlock { Text = $"{role} ({token})", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, row, 0);
            Put(resolution, row, 1);
            Put(fps, row, 2);
            Put(bitrate, row, 3);
            Put(new TextBlock
            {
                Text = $"{settings.Width}×{settings.Height} · {settings.Fps} fps · {settings.BitrateKbps} kbps",
                Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center,
            }, row, 4);
            _rows.Add(editor);
        }
    }

    static void Fill(EditorRow row, EncoderChange values)
    {
        var match = row.Resolution.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is (int w, int h) && w == values.Width && h == values.Height);
        row.Resolution.SelectedItem = match ?? row.Resolution.Items.OfType<ComboBoxItem>().FirstOrDefault();
        row.Fps.Text = values.Fps.ToString(CultureInfo.InvariantCulture);
        row.Bitrate.Text = values.BitrateKbps.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Light values for a weak link: main about 720p, sub about 360p, 10 fps, 768 / 256 kbps.</summary>
    void Recommended_Click(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var main = i == 0 && _rows.Count > 1;
            var maxHeight = main ? 720 : 360;
            var choices = row.Resolution.Items.OfType<ComboBoxItem>().Select(item => ((int W, int H))item.Tag).ToList();
            var fitting = choices.Where(r => r.H <= maxHeight).OrderByDescending(r => r.W * r.H).ToList();
            var (w, h) = fitting.Count > 0 ? fitting[0] : choices.OrderBy(r => r.W * r.H).First();
            var fps = Math.Clamp(10, Math.Max(1, row.Options?.FpsMin ?? 1), row.Options is { FpsMax: > 0 } o ? o.FpsMax : 30);
            Fill(row, new EncoderChange(w, h, fps, main ? 768 : 256));
        }
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        var originals = EncoderSnapshot.Parse(_camera.OriginalEncoders);
        foreach (var row in _rows)
            if (originals.TryGetValue(row.Token, out var values)) Fill(row, values);
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var changes = new List<(EditorRow Row, EncoderChange Change)>();
        foreach (var row in _rows)
        {
            if (row.Resolution.SelectedItem is not ComboBoxItem { Tag: (int w, int h) }
                || !int.TryParse(row.Fps.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var fps)
                || !int.TryParse(row.Bitrate.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var kbps))
            {
                MessageBox.Show(this, $"Revisa los valores de {row.Token}: fps y bitrate deben ser números enteros.", "Centinela",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var (fpsMin, fpsMax) = (Math.Max(1, row.Options?.FpsMin ?? 1), row.Options is { FpsMax: > 0 } o ? o.FpsMax : 30);
            if (fps < fpsMin || fps > fpsMax || kbps < MinBitrate || kbps > MaxBitrate)
            {
                MessageBox.Show(this, $"{row.Token}: los fps deben estar entre {fpsMin} y {fpsMax}, y el bitrate entre {MinBitrate} y {MaxBitrate} kbps.",
                    "Centinela", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var change = new EncoderChange(w, h, fps, kbps);
            var current = new EncoderChange(row.Current.Width, row.Current.Height, row.Current.Fps, row.Current.BitrateKbps);
            if (change != current) changes.Add((row, change));
        }
        if (changes.Count == 0)
        {
            MessageBox.Show(this, "No hay cambios que aplicar.", "Centinela", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var summary = string.Join("\n", changes.Select(c =>
            $"{c.Row.Token}:  {c.Row.Current.Width}×{c.Row.Current.Height} · {c.Row.Current.Fps} fps · {c.Row.Current.BitrateKbps} kbps"
            + $"  →  {c.Change.Width}×{c.Change.Height} · {c.Change.Fps} fps · {c.Change.BitrateKbps} kbps"));
        var answer = MessageBox.Show(this,
            $"Se cambiará en la cámara «{_camera.Name}»:\n\n{summary}\n\nSe guarda en la cámara (también afecta a la app Imou y a sus grabaciones) y la imagen se cortará unos segundos. ¿Aplicar?",
            "Centinela", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        // Remember what the camera had before Centinela's first change, for «Valores originales».
        if (EncoderSnapshot.Parse(_camera.OriginalEncoders).Count == 0)
        {
            _camera.OriginalEncoders = EncoderSnapshot.Format(_rows.Select(r => (r.Token, r.Current)));
            _save();
        }

        _query?.Cancel();
        SetBusy(true);
        EditBox.IsEnabled = false;
        var log = new StringBuilder("── Cambios aplicados ──\n");
        Report.Text = log + "Aplicando…";
        using var http = CreateOnvifHttp();
        var client = new OnvifClient(http, DeviceUrl, _camera.User, _camera.Password);
        foreach (var (row, change) in changes)
        {
            try
            {
                await client.SetEncoderAsync(row.Token, change);
                log.AppendLine($"{row.Token}: enviado {change.Width}×{change.Height} · {change.Fps} fps · {change.BitrateKbps} kbps — aceptado.");
            }
            catch (Exception ex) when (ex is OnvifException or HttpRequestException or TaskCanceledException or System.Xml.XmlException)
            {
                log.AppendLine($"{row.Token}: la cámara no lo aceptó — {Explain(ex)}");
            }
            Report.Text = log.ToString();
        }
        log.AppendLine("(Abajo, lo que la cámara dice tener ahora: si no coincide, lo ha ignorado.)");
        log.AppendLine();
        _log = log.ToString();
        await QueryAsync(includeApi: false);
    }

    static string HostOf(Camera camera)
    {
        if (camera.Host.Trim().Length > 0) return camera.Host.Trim();
        return Uri.TryCreate(camera.MainUrlOverride ?? camera.SubUrlOverride, UriKind.Absolute, out var url) ? url.Host : "";
    }

    static string Describe(EncoderSettings s)
    {
        var parts = new List<string> { s.Name.Length > 0 ? s.Name : "(sin nombre)" };
        if (s.Codec.Length > 0) parts.Add(s.Codec);
        if (s.Width > 0) parts.Add($"{s.Width}×{s.Height}");
        if (s.Fps > 0) parts.Add($"{s.Fps} fps");
        if (s.BitrateKbps > 0) parts.Add($"{s.BitrateKbps} kbps" + (s.RateControl is { Length: > 0 } rc ? $" {rc}" : ""));
        if (s.Gop > 0) parts.Add($"GOP {s.Gop}");
        return string.Join(" · ", parts);
    }

    static string Range(int min, int max) =>
        max <= 0 ? "?" : string.Create(CultureInfo.InvariantCulture, $"{min}–{max}");

    static string Explain(Exception ex) => ex switch
    {
        TaskCanceledException => "no contesta (tiempo agotado)",
        HttpRequestException http => CredentialSanitizer.Sanitize(http.Message),
        _ => CredentialSanitizer.Sanitize(ex.Message),
    };

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        _log = "";
        _ = QueryAsync(includeApi: true);
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Report.Text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: the user can try again */ }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
