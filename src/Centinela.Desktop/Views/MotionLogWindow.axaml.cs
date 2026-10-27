using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>«Registro de movimiento»: newest-first view of the motion log that follows new entries while open.</summary>
public partial class MotionLogWindow : Window
{
    /// <summary>A duration as the motion log line shows it (<c>m:ss</c> / <c>h:mm:ss</c>).</summary>
    public static readonly IValueConverter DurationText = new FuncValueConverter<TimeSpan, string>(MotionLog.FormatDuration);

    /// <summary>The peak changed fraction as a percentage (<c>4.2 %</c>), as the Windows version shows it.</summary>
    public static readonly IValueConverter PeakText =
        new FuncValueConverter<double, string>(peak => peak.ToString("0.0 %", CultureInfo.InvariantCulture));

    readonly MotionLog? _log;
    readonly ObservableCollection<MotionLogEntry> _rows = [];
    bool _closed;

    public MotionLogWindow() => InitializeComponent();

    public MotionLogWindow(MotionLog log) : this()
    {
        _log = log;
        foreach (var entry in log.Snapshot()) _rows.Add(entry);
        Rows.ItemsSource = _rows;
        log.EntryAdded += OnEntryAdded;
        Closed += (_, _) =>
        {
            _closed = true;
            log.EntryAdded -= OnEntryAdded;
        };
    }

    // EntryAdded fires on the caller's thread.
    void OnEntryAdded(MotionLogEntry entry) => UiThread.Post(() =>
    {
        if (!_closed) _rows.Insert(0, entry);
    });

    async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var selected = Rows.SelectedItems?.OfType<MotionLogEntry>().ToHashSet() ?? [];
        // In the order shown (newest first), whatever order they were selected in.
        var rows = selected.Count > 0 ? _rows.Where(selected.Contains) : _rows;
        var text = string.Join(Environment.NewLine, rows.Select(MotionLog.FormatLine));
        if (text.Length == 0 || Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy in another app */ }
    }

    void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        App.Platform.Files.OpenFolder(AppPaths.LogsDirectory);
    }

    void Clear_Click(object? sender, RoutedEventArgs e)
    {
        _log?.Clear();
        _rows.Clear();
    }
}
