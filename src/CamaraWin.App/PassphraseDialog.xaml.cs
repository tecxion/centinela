using System.Windows;

namespace CamaraWin.App;

/// <summary>Asks for a backup passphrase; in confirm mode it must be typed twice and have at least 8 characters.</summary>
public partial class PassphraseDialog : Window
{
    public const int MinimumLength = 8;

    readonly bool _confirm;

    PassphraseDialog(bool confirm)
    {
        InitializeComponent();
        _confirm = confirm;
        ConfirmPanel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => First.Focus();
    }

    public static string? Ask(Window owner, bool confirm)
    {
        var dialog = new PassphraseDialog(confirm) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.First.Password : null;
    }

    /// <summary>Validation shared with <see cref="ExportDialog"/>; returns the error to show, or null when valid.</summary>
    internal static string? Validate(string first, string? second)
    {
        if (second is not null && first.Length < MinimumLength) return $"La clave debe tener al menos {MinimumLength} caracteres.";
        if (second is not null && first != second) return "Las claves no coinciden.";
        if (first.Length == 0) return "Escribe la clave.";
        return null;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Validate(First.Password, _confirm ? Second.Password : null) is { } error)
        {
            Error.Text = error;
            return;
        }
        DialogResult = true;
    }
}
