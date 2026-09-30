using Centinela.Core;

namespace Centinela.Desktop.Services.Cameras;

/// <summary>
/// «Editar cámara» works on a copy taken when it opened, but the dialog only blocks its owner window: HD/SD,
/// «Suavizar imagen», 👁 and the rest can still change from other windows meanwhile. Saving must not undo them.
/// </summary>
static class EditedCamera
{
    /// <summary>
    /// The camera to store: the dialog's fields from <paramref name="edited"/>, and everything the dialog does not
    /// edit from <paramref name="current"/> (the camera as stored now). Returns a new copy; neither input changes.
    /// </summary>
    public static Camera Merge(Camera edited, Camera current)
    {
        var merged = edited.Clone();
        merged.LowQualityWhenBig = current.LowQualityWhenBig;
        merged.Smoothing = current.Smoothing;
        merged.MotionEnabled = current.MotionEnabled;
        merged.MotionSensitivity = current.MotionSensitivity;
        merged.MotionCooldownSeconds = current.MotionCooldownSeconds;
        merged.ConnectionAlerts = current.ConnectionAlerts;
        merged.MotionAlerts = current.MotionAlerts;
        merged.OriginalEncoders = current.OriginalEncoders;
        merged.Order = current.Order;
        return merged;
    }
}
