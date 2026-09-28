using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Centinela.Core;

namespace Centinela.App;

/// <summary>Newest-first view of the motion log that follows new entries while open.</summary>
public partial class MotionLogWindow : Window
{
    readonly MotionLog _log;
    readonly ObservableCollection<MotionLogEntry> _rows;

    public MotionLogWindow(MotionLog log)
    {
        InitializeComponent();
        _log = log;
        _rows = new ObservableCollection<MotionLogEntry>(log.Snapshot());
        EntriesGrid.ItemsSource = _rows;
        log.EntryAdded += OnEntryAdded;
        Closed += (_, _) => log.EntryAdded -= OnEntryAdded;
    }

    // EntryAdded fires on the caller's thread.
    void OnEntryAdded(MotionLogEntry entry) => Dispatcher.BeginInvoke(() => _rows.Insert(0, entry));

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var rows = EntriesGrid.SelectedItems.Count > 0 ? EntriesGrid.SelectedItems.Cast<MotionLogEntry>() : _rows;
        var text = string.Join(Environment.NewLine, rows.Select(MotionLog.FormatLine));
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

/// <summary>Shows a <see cref="TimeSpan"/> as the motion log line does (<c>m:ss</c> / <c>h:mm:ss</c>).</summary>
public sealed class MotionDurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TimeSpan duration ? MotionLog.FormatDuration(duration) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
