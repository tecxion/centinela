using Centinela.Core;
using Centinela.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>A choice in a drop-down: the value and the text shown.</summary>
sealed record OptionItem<T>(T Value, string Label);

/// <summary>
/// One camera's notice options in «Opciones de avisos»; a copy, written back only on «Aceptar». The drop-down values
/// are the same instances as <see cref="NotificationOptions.Sensitivities"/> and <see cref="NotificationOptions.Cooldowns"/>,
/// so the drop-downs show them selected.
/// </summary>
sealed partial class NotificationOptionsRow : ObservableObject
{
    public NotificationOptionsRow(Camera camera)
    {
        Id = camera.Id;
        Name = camera.Name;
        _motionEnabled = camera.MotionEnabled;
        _sensitivity = NotificationOptions.Sensitivities.FirstOrDefault(s => s.Value == camera.MotionSensitivity)
                       ?? NotificationOptions.Sensitivities[1];
        var cooldown = Camera.NormalizeCooldown(camera.MotionCooldownSeconds);
        _cooldown = NotificationOptions.Cooldowns.First(c => c.Value == cooldown);
        _connectionAlerts = camera.ConnectionAlerts;
        _motionAlerts = camera.MotionAlerts;
    }

    public Guid Id { get; }
    public string Name { get; }

    [ObservableProperty] private bool _motionEnabled;
    [ObservableProperty] private OptionItem<MotionSensitivity> _sensitivity;
    [ObservableProperty] private OptionItem<int> _cooldown;
    [ObservableProperty] private bool _connectionAlerts;
    [ObservableProperty] private bool _motionAlerts;
}
