using System.Windows;

namespace CamaraWin.App;

/// <summary>Export options: with or without passwords (encrypted with a confirmed passphrase).</summary>
public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    /// <summary>Set after <c>ShowDialog() == true</c>.</summary>
    public bool IncludePasswords { get; private set; }

    /// <summary>The passphrase when <see cref="IncludePasswords"/> is true; otherwise null.</summary>
    public string? Passphrase { get; private set; }

    void Include_Changed(object sender, RoutedEventArgs e)
    {
        KeyPanel.IsEnabled = IncludeBox.IsChecked == true;
        Error.Text = "";
        if (KeyPanel.IsEnabled) First.Focus();
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var include = IncludeBox.IsChecked == true;
        if (include && PassphraseDialog.Validate(First.Password, Second.Password) is { } error)
        {
            Error.Text = error;
            return;
        }
        IncludePasswords = include;
        Passphrase = include ? First.Password : null;
        DialogResult = true;
    }
}
