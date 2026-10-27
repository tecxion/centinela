using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>«Registro de errores»: newest-first view of the error log that follows new entries while open.</summary>
public partial class ErrorLogWindow : Window
{
    readonly ErrorLog? _log;
    readonly ObservableCollection<ErrorLogEntry> _rows = [];
    bool _closed;

    public ErrorLogWindow() => InitializeComponent();

    public ErrorLogWindow(ErrorLog log) : this()
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
    void OnEntryAdded(ErrorLogEntry entry) => UiThread.Post(() =>
    {
        if (!_closed) _rows.Insert(0, entry);
    });

    async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var selected = Rows.SelectedItems?.OfType<ErrorLogEntry>().ToHashSet() ?? [];
        // In the order shown (newest first), whatever order they were selected in.
        var rows = selected.Count > 0 ? _rows.Where(selected.Contains) : _rows;
        var text = string.Join(Environment.NewLine, rows.Select(ErrorLog.FormatLine));
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
