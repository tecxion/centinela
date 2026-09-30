using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Views;

/// <summary>Export options: with or without passwords (encrypted with a confirmed passphrase).</summary>
public partial class ExportDialog : Window
{
    public ExportDialog() => InitializeComponent();

    /// <summary>Set after <c>ShowDialog&lt;bool&gt;(owner) == true</c>.</summary>
    public bool IncludePasswords { get; private set; }

    /// <summary>The passphrase when <see cref="IncludePasswords"/> is true; otherwise null.</summary>
    public string? Passphrase { get; private set; }

    void Include_Changed(object? sender, RoutedEventArgs e)
    {
        KeyPanel.IsEnabled = IncludeBox.IsChecked == true;
        Error.Text = "";
        if (KeyPanel.IsEnabled) First.Focus();
    }

    void Export_Click(object? sender, RoutedEventArgs e)
    {
        var include = IncludeBox.IsChecked == true;
        var first = First.Text ?? "";
        if (include && PassphraseRules.Validate(first, Second.Text ?? "") is { } error)
        {
            Error.Text = error;
            return;
        }
        IncludePasswords = include;
        Passphrase = include ? first : null;
        Close(true);
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
