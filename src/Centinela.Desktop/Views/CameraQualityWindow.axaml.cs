using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Quality;

namespace Centinela.Desktop.Views;

/// <summary>
/// «Calidad de la cámara…»: reports the camera's encoder settings and what it accepts (ONVIF and the Dahua/Imou
/// HTTP API) and lets the user change resolution, fps and bitrate through ONVIF. Nothing is written without
/// «Aplicar…» and its confirmation. The values found before Centinela's first change are handed to
/// <c>saveOriginals</c> for «Valores originales». The report never contains the password.
/// </summary>
public partial class CameraQualityWindow : Window
{
    readonly Camera _camera = new();
    readonly Action<string> _saveOriginals = _ => { };
    readonly QualityQuery _quality = new(new Camera());
    CancellationTokenSource? _query;
    string _log = ""; // results of the last «Aplicar», shown above the report
    bool _closed;

    // Parameterless constructor for the XAML loader and designer.
    public CameraQualityWindow() => InitializeComponent();

    /// <param name="camera">The camera as it is now (only read here).</param>
    /// <param name="saveOriginals">Receives the <see cref="EncoderSnapshot"/> text the first time a change is applied.</param>
    public CameraQualityWindow(Camera camera, Action<string> saveOriginals) : this()
    {
        _camera = camera;
        _saveOriginals = saveOriginals;
        _quality = new QualityQuery(camera);
        Title = $"Calidad de la cámara — {camera.Name}";
        Opened += (_, _) => _ = QueryAsync(includeApi: true);
        Closed += (_, _) =>
        {
            _closed = true;
            _query?.Cancel();
        };
    }

    async Task QueryAsync(bool includeApi)
    {
        _query?.Cancel();
        var query = _query = new CancellationTokenSource();
        SetBusy(true);
        var report = new StringBuilder(_log);
        void Line(string text)
        {
            report.AppendLine(text);
            if (!query.IsCancellationRequested) Report.Text = report.ToString();
        }

        IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)> configurations;
        try
        {
            configurations = await _quality.RunAsync(includeApi, Line, query.Token);
        }
        catch (OperationCanceledException)
        {
            configurations = [];
        }
        // Closed, or replaced by a newer query or by «Aplicar».
        if (_closed || query != _query) return;
        BuildEditors(configurations);
        SetBusy(false);
    }

    void SetBusy(bool busy)
    {
        RetryButton.IsEnabled = !busy;
        EditBox.IsEnabled = !busy && _rows.Count > 0;
        RestoreButton.IsEnabled = EncoderSnapshot.Parse(_quality.Originals).Count > 0;
    }

    async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        var changes = new List<(EditorRow Row, EncoderChange Change)>();
        foreach (var row in _rows)
        {
            var error = QualityEdits.TryRead(row.Token, row.Selected, row.Fps.Text ?? "", row.Bitrate.Text ?? "", row.Options,
                out var change);
            if (error is not null)
            {
                await MessageDialog.ShowAsync(this, error);
                return;
            }
            if (change != Now(row)) changes.Add((row, change));
        }
        if (changes.Count == 0)
        {
            await MessageDialog.ShowAsync(this, "No hay cambios que aplicar.");
            return;
        }

        var summary = string.Join("\n", changes.Select(c =>
            $"{c.Row.Token}:  {QualityText.Values(Now(c.Row))}  →  {QualityText.Values(c.Change)}"));
        if (!await MessageDialog.ConfirmAsync(this,
                $"Se cambiará en la cámara «{_camera.Name}»:\n\n{summary}\n\nSe guarda en la cámara (también afecta a la app Imou y a sus grabaciones) y la imagen se cortará unos segundos. ¿Aplicar?"))
            return;
        if (_closed) return;

        // Remember what the camera had before Centinela's first change, for «Valores originales».
        if (EncoderSnapshot.Parse(_quality.Originals).Count == 0)
        {
            var originals = EncoderSnapshot.Format(_rows.Select(r => (r.Token, r.Current)));
            _quality.Originals = originals;
            _saveOriginals(originals);
        }

        // A query still running must not re-enable the editors while the changes are sent.
        _query?.Cancel();
        _query = null;
        SetBusy(true);
        EditBox.IsEnabled = false;
        var log = new StringBuilder().AppendLine("── Cambios aplicados ──");
        Report.Text = log + "Aplicando…";
        foreach (var line in await _quality.ApplyAsync(changes.Select(c => (c.Row.Token, c.Change)).ToList()))
            log.AppendLine(line);
        log.AppendLine("(Abajo, lo que la cámara dice tener ahora: si no coincide, lo ha ignorado.)");
        log.AppendLine();
        _log = log.ToString();
        if (_closed) return;
        Report.Text = _log;
        await QueryAsync(includeApi: false);
    }

    static EncoderChange Now(EditorRow row) =>
        new(row.Current.Width, row.Current.Height, row.Current.Fps, row.Current.BitrateKbps);

    void Retry_Click(object? sender, RoutedEventArgs e)
    {
        _log = "";
        _ = QueryAsync(includeApi: true);
    }

    async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(Report.Text ?? ""); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: the user can try again */ }
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
