using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Centinela.Core.Onvif;
using Centinela.Desktop.Services.Quality;

namespace Centinela.Desktop.Views;

/// <summary>The «Cambiar calidad (ONVIF)» table: one editor row per encoder configuration, and its fill buttons.</summary>
public partial class CameraQualityWindow
{
    readonly List<EditorRow> _rows = [];

    sealed record EditorRow(string Token, EncoderSettings Current, EncoderOptions? Options,
        IReadOnlyList<(int W, int H)> Choices, ComboBox Resolution, TextBox Fps, TextBox Bitrate)
    {
        public (int W, int H)? Selected =>
            Resolution.SelectedItem is ComboBoxItem { Tag: (int w, int h) } ? (w, h) : null;
    }

    /// <summary>One row per ONVIF encoder configuration (largest first), pre-filled with its current values.</summary>
    void BuildEditors(IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)> configurations)
    {
        _rows.Clear();
        Editors.Children.Clear();
        Editors.RowDefinitions.Clear();
        Editors.ColumnDefinitions.Clear();
        foreach (var width in new[] { 150.0, 140, 70, 110 })
            Editors.ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Pixel));
        Editors.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        void Put(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Editors.Children.Add(control);
        }
        Editors.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] headers = ["Flujo", "Resolución", "fps", "Bitrate (kbps)", "Ahora"];
        for (var c = 0; c < headers.Length; c++)
            Put(new TextBlock { Text = headers[c], FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 8, 4) }, 0, c);

        foreach (var (token, settings, options) in configurations.OrderByDescending(c => c.Settings.Width * c.Settings.Height))
        {
            var row = Editors.RowDefinitions.Count;
            Editors.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var choices = QualityEdits.Choices(settings, options);
            var resolution = new ComboBox { Margin = new Thickness(0, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var r in choices) resolution.Items.Add(new ComboBoxItem { Content = $"{r.W}×{r.H}", Tag = r });
            var fps = new TextBox { Margin = new Thickness(0, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
            var bitrate = new TextBox { Margin = new Thickness(0, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
            var editor = new EditorRow(token, settings, options, choices, resolution, fps, bitrate);
            var now = new EncoderChange(settings.Width, settings.Height, settings.Fps, settings.BitrateKbps);
            Fill(editor, now);

            var role = row == 1 ? "Principal" : "Secundario";
            Put(new TextBlock { Text = $"{role} ({token})", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, row, 0);
            Put(resolution, row, 1);
            Put(fps, row, 2);
            Put(bitrate, row, 3);
            Put(new TextBlock { Text = QualityText.Values(now), Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center }, row, 4);
            _rows.Add(editor);
        }
    }

    static void Fill(EditorRow row, EncoderChange values)
    {
        var items = row.Resolution.Items.OfType<ComboBoxItem>().ToList();
        var match = items.FirstOrDefault(i => i.Tag is (int w, int h) && w == values.Width && h == values.Height);
        row.Resolution.SelectedItem = match ?? items.FirstOrDefault();
        row.Fps.Text = values.Fps.ToString(CultureInfo.InvariantCulture);
        row.Bitrate.Text = values.BitrateKbps.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Light values for a weak link; nothing changes on the camera until «Aplicar…».</summary>
    void Recommended_Click(object? sender, RoutedEventArgs e)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            Fill(row, QualityEdits.Recommended(row.Choices, row.Options, main: i == 0 && _rows.Count > 1));
        }
    }

    void Restore_Click(object? sender, RoutedEventArgs e)
    {
        var originals = EncoderSnapshot.Parse(_quality.Originals);
        foreach (var row in _rows)
            if (originals.TryGetValue(row.Token, out var values)) Fill(row, values);
    }
}
