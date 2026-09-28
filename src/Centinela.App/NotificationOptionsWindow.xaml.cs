using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Centinela.Core;

namespace Centinela.App;

/// <summary>One camera's notice options in the grid; a copy, written back only on «Aceptar».</summary>
public sealed class NotificationOptionsRow(Camera camera)
{
    public Guid Id { get; } = camera.Id;
    public string Name { get; } = camera.Name;
    public bool MotionEnabled { get; set; } = camera.MotionEnabled;
    public MotionSensitivity Sensitivity { get; set; } = camera.MotionSensitivity;
    public int CooldownSeconds { get; set; } = Camera.NormalizeCooldown(camera.MotionCooldownSeconds);
    public bool ConnectionAlerts { get; set; } = camera.ConnectionAlerts;
    public bool MotionAlerts { get; set; } = camera.MotionAlerts;
}

public sealed record OptionItem<T>(T Value, string Label);

/// <summary>
/// «Opciones de avisos…»: per-camera detection and alerts, sounds and quiet hours. Works on copies: nothing changes
/// until the caller reads <see cref="Rows"/> and the settings properties after <c>ShowDialog() == true</c>.
/// </summary>
public partial class NotificationOptionsWindow : Window
{
    public static IReadOnlyList<OptionItem<MotionSensitivity>> Sensitivities { get; } =
    [
        new(MotionSensitivity.Low, "Baja"),
        new(MotionSensitivity.Medium, "Media"),
        new(MotionSensitivity.High, "Alta"),
    ];

    public static IReadOnlyList<OptionItem<int>> Cooldowns { get; } =
        [.. Camera.AllowedCooldowns.Select(s => new OptionItem<int>(s, s < 60 ? $"{s} s" : $"{s / 60} min"))];

    readonly Brush _normalBorder;

    /// <param name="cameras">Read only: each row copies its camera's values.</param>
    /// <param name="settings">Read only: the check boxes and times start from these values.</param>
    public NotificationOptionsWindow(IEnumerable<Camera> cameras, AppSettings settings)
    {
        InitializeComponent();
        _normalBorder = QuietFromBox.BorderBrush;
        Rows = [.. cameras.OrderBy(c => c.Order).Select(c => new NotificationOptionsRow(c))];
        Grid.ItemsSource = Rows;
        SoundLostBox.IsChecked = settings.SoundOnConnectionLost;
        SoundMotionBox.IsChecked = settings.SoundOnMotion;
        QuietBox.IsChecked = settings.QuietHoursEnabled;
        QuietFromBox.Text = settings.QuietFrom;
        QuietToBox.Text = settings.QuietTo;
    }

    public IReadOnlyList<NotificationOptionsRow> Rows { get; }

    // Set by «Aceptar» (valid times only).
    public bool SoundOnConnectionLost { get; private set; }
    public bool SoundOnMotion { get; private set; }
    public bool QuietHoursEnabled { get; private set; }
    public string QuietFrom { get; private set; } = "";
    public string QuietTo { get; private set; } = "";

    void Time_Changed(object sender, TextChangedEventArgs e)
    {
        // Fires during InitializeComponent/constructor too, before the normal border is known.
        if (sender is not TextBox box || _normalBorder is null) return;
        if (Mark(box) && Mark(box == QuietFromBox ? QuietToBox : QuietFromBox)) Error.Text = "";
    }

    /// <summary>Red border on an invalid time; true when valid.</summary>
    bool Mark(TextBox box)
    {
        var valid = AppSettings.TryParseTime(box.Text.Trim(), out _);
        box.BorderBrush = valid ? _normalBorder : Brushes.Red;
        return valid;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Commit a cell still being edited before the caller reads the rows.
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var fromValid = Mark(QuietFromBox);
        var toValid = Mark(QuietToBox);
        if (!fromValid || !toValid)
        {
            Error.Text = "Escribe las horas de silencio con el formato HH:mm (por ejemplo 23:00).";
            (fromValid ? QuietToBox : QuietFromBox).Focus();
            return;
        }
        SoundOnConnectionLost = SoundLostBox.IsChecked == true;
        SoundOnMotion = SoundMotionBox.IsChecked == true;
        QuietHoursEnabled = QuietBox.IsChecked == true;
        QuietFrom = QuietFromBox.Text.Trim();
        QuietTo = QuietToBox.Text.Trim();
        DialogResult = true;
    }
}
