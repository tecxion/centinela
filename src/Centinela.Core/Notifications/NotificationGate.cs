namespace Centinela.Core;

public enum NoticeKind { ConnectionLost, ConnectionRecovered, Motion }

public readonly record struct NoticeDecision(bool Show, bool PlaySound);

/// <summary>
/// Whether a notice is shown and a sound played. Logs and the red frame never pass through here.
/// Motion notices are only for a window that is not visible (minimized or in the tray).
/// </summary>
public static class NotificationGate
{
    public static NoticeDecision Decide(NoticeKind kind, Camera camera, AppSettings settings, bool windowVisible, DateTime now)
    {
        if (IsQuiet(settings, now.TimeOfDay)) return default;
        return kind switch
        {
            NoticeKind.ConnectionLost when camera.ConnectionAlerts => new(true, settings.SoundOnConnectionLost),
            NoticeKind.ConnectionRecovered when camera.ConnectionAlerts => new(true, false),
            NoticeKind.Motion when camera.MotionAlerts && !windowVisible => new(true, settings.SoundOnMotion),
            _ => default,
        };
    }

    /// <summary>[From, To) local time; crosses midnight when From &gt; To; equal times mean no quiet hours.</summary>
    public static bool IsQuiet(AppSettings settings, TimeSpan timeOfDay)
    {
        if (!settings.QuietHoursEnabled
            || !AppSettings.TryParseTime(settings.QuietFrom, out var from)
            || !AppSettings.TryParseTime(settings.QuietTo, out var to)
            || from == to) return false;
        return from < to ? timeOfDay >= from && timeOfDay < to : timeOfDay >= from || timeOfDay < to;
    }
}
