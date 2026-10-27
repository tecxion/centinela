using Centinela.Core;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Services.Notifications;

/// <summary>What «Opciones de avisos» chose for the sounds and quiet hours (times as typed).</summary>
sealed record QuietHours(bool SoundOnConnectionLost, bool SoundOnMotion, bool Enabled, string From, string To);

/// <summary>The rules of «Opciones de avisos…»: drop-down choices, the rows, time validation and applying the result.</summary>
static class NotificationOptions
{
    public const string TimeError = "Escribe las horas de silencio con el formato HH:mm (por ejemplo 23:00).";

    public static IReadOnlyList<OptionItem<MotionSensitivity>> Sensitivities { get; } =
    [
        new(MotionSensitivity.Low, "Baja"),
        new(MotionSensitivity.Medium, "Media"),
        new(MotionSensitivity.High, "Alta"),
    ];

    public static IReadOnlyList<OptionItem<int>> Cooldowns { get; } =
        [.. Camera.AllowedCooldowns.Select(s => new OptionItem<int>(s, s < 60 ? $"{s} s" : $"{s / 60} min"))];

    /// <summary>One row per camera, in their order; each a copy of the camera's values.</summary>
    public static IReadOnlyList<NotificationOptionsRow> RowsFor(IEnumerable<Camera> cameras) =>
        [.. cameras.OrderBy(c => c.Order).Select(c => new NotificationOptionsRow(c))];

    /// <summary>"HH:mm" (00:00–23:59) once trimmed.</summary>
    public static bool IsValidTime(string? text) => AppSettings.TryParseTime((text ?? "").Trim(), out _);

    /// <summary>
    /// Copies each row to the camera with its Id (rows of cameras no longer there are ignored) and the sounds and
    /// quiet hours to the settings (times trimmed). Does not save.
    /// </summary>
    public static void Apply(IEnumerable<NotificationOptionsRow> rows, IEnumerable<Camera> cameras, AppSettings settings,
        QuietHours sounds)
    {
        var byId = cameras.ToDictionary(c => c.Id);
        foreach (var row in rows)
        {
            if (!byId.TryGetValue(row.Id, out var camera)) continue;
            camera.MotionEnabled = row.MotionEnabled;
            camera.MotionSensitivity = row.Sensitivity.Value;
            camera.MotionCooldownSeconds = Camera.NormalizeCooldown(row.Cooldown.Value);
            camera.ConnectionAlerts = row.ConnectionAlerts;
            camera.MotionAlerts = row.MotionAlerts;
        }
        settings.SoundOnConnectionLost = sounds.SoundOnConnectionLost;
        settings.SoundOnMotion = sounds.SoundOnMotion;
        settings.QuietHoursEnabled = sounds.Enabled;
        settings.QuietFrom = sounds.From.Trim();
        settings.QuietTo = sounds.To.Trim();
    }
}
