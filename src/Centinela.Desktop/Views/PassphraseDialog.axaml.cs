using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Views;

/// <summary>Asks for a backup passphrase; in confirm mode it must be typed twice and have at least 8 characters.</summary>
public partial class PassphraseDialog : Window
{
    readonly bool _confirm;

    public PassphraseDialog() : this(confirm: false) { }

    PassphraseDialog(bool confirm)
    {
        InitializeComponent();
        _confirm = confirm;
        ConfirmPanel.IsVisible = confirm;
        Opened += (_, _) => First.Focus();
    }

    /// <summary>The passphrase typed, or null when cancelled.</summary>
    public static Task<string?> AskAsync(Window owner, bool confirm) => new PassphraseDialog(confirm).ShowDialog<string?>(owner);

    void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var first = First.Text ?? "";
        if (PassphraseRules.Validate(first, _confirm ? Second.Text ?? "" : null) is { } error)
        {
            Error.Text = error;
            return;
        }
        Close(first);
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
