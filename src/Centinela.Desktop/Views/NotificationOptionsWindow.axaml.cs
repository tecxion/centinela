using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Notifications;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

/// <summary>
/// «Opciones de avisos…»: per-camera detection and alerts, sounds and quiet hours. Works on copies: nothing changes
/// until the caller applies <see cref="Rows"/> and <see cref="Sounds"/> after <c>ShowDialog&lt;bool&gt;() == true</c>.
/// </summary>
public partial class NotificationOptionsWindow : Window
{
    public NotificationOptionsWindow() => InitializeComponent();

    /// <param name="cameras">Read only: each row copies its camera's values.</param>
    /// <param name="settings">Read only: the check boxes and times start from these values.</param>
    internal NotificationOptionsWindow(IEnumerable<Camera> cameras, AppSettings settings) : this()
    {
        Rows = NotificationOptions.RowsFor(cameras);
        RowsList.ItemsSource = Rows;
        SoundLostBox.IsChecked = settings.SoundOnConnectionLost;
        SoundMotionBox.IsChecked = settings.SoundOnMotion;
        QuietBox.IsChecked = settings.QuietHoursEnabled;
        QuietFromBox.Text = settings.QuietFrom;
        QuietToBox.Text = settings.QuietTo;
        QuietFromBox.TextChanged += Time_Changed;
        QuietToBox.TextChanged += Time_Changed;
    }

    internal IReadOnlyList<NotificationOptionsRow> Rows { get; } = [];

    /// <summary>Set by «Aceptar» (valid times only).</summary>
    internal QuietHours? Sounds { get; private set; }

    void Time_Changed(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (Mark(box) && Mark(box == QuietFromBox ? QuietToBox : QuietFromBox)) Error.Text = "";
    }

    /// <summary>Red border on an invalid time; true when valid.</summary>
    static bool Mark(TextBox box)
    {
        var valid = NotificationOptions.IsValidTime(box.Text);
        box.Classes.Set("invalid", !valid);
        return valid;
    }

    void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var fromValid = Mark(QuietFromBox);
        var toValid = Mark(QuietToBox);
        if (!fromValid || !toValid)
        {
            Error.Text = NotificationOptions.TimeError;
            (fromValid ? QuietToBox : QuietFromBox).Focus();
            return;
        }
        Sounds = new QuietHours(SoundLostBox.IsChecked == true, SoundMotionBox.IsChecked == true,
            QuietBox.IsChecked == true, QuietFromBox.Text ?? "", QuietToBox.Text ?? "");
        Close(true);
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
