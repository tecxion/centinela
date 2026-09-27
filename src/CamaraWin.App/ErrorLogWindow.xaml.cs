using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CamaraWin.Core;

namespace CamaraWin.App;

/// <summary>Newest-first view of the error log that follows new entries while open.</summary>
public partial class ErrorLogWindow : Window
{
    readonly ErrorLog _log;
    readonly ObservableCollection<ErrorLogEntry> _rows;

    public ErrorLogWindow(ErrorLog log)
    {
        InitializeComponent();
        _log = log;
        _rows = new ObservableCollection<ErrorLogEntry>(log.Snapshot());
        Grid.ItemsSource = _rows;
        log.EntryAdded += OnEntryAdded;
        Closed += (_, _) => log.EntryAdded -= OnEntryAdded;
    }

    // EntryAdded fires on the caller's thread.
    void OnEntryAdded(ErrorLogEntry entry) => Dispatcher.BeginInvoke(() => _rows.Insert(0, entry));

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var rows = Grid.SelectedItems.Count > 0 ? Grid.SelectedItems.Cast<ErrorLogEntry>() : _rows;
        var text = string.Join(Environment.NewLine, rows.Select(ErrorLog.FormatLine));
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy in another app */ }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Process.Start("explorer.exe", $"\"{AppPaths.LogsDirectory}\"");
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        _rows.Clear();
    }
}
