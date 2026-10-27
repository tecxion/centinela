namespace Centinela.Core;

public enum Brand { Tapo, Imou, Custom }

public enum StreamKind { Main, Sub }

public enum MotionSensitivity { Low, Medium, High }

public sealed class Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Brand Brand { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 554;
    public string User { get; set; } = "";
    /// <summary>Plaintext, in memory only. Encrypted by CameraStore on disk.</summary>
    public string Password { get; set; } = "";
    public string? MainUrlOverride { get; set; }
    public string? SubUrlOverride { get; set; }
    public bool UseUdp { get; set; }
    public int Order { get; set; }
    public bool MotionEnabled { get; set; }
    public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;
    /// <summary>Minimum time between two motion notices of this camera; one of <see cref="AllowedCooldowns"/>.</summary>
    public int MotionCooldownSeconds { get; set; } = 60;
    public bool ConnectionAlerts { get; set; } = true;
    public bool MotionAlerts { get; set; } = true;
    /// <summary>Shown big with the substream: for cameras whose link cannot carry the main stream smoothly.</summary>
    public bool LowQualityWhenBig { get; set; }
    /// <summary>Live view played 1–2 s behind but evenly paced: for a link that delivers in stalls and bursts.</summary>
    public bool Smoothing { get; set; }
    /// <summary>
    /// Encoder values the camera had before Centinela first changed them (<see cref="Onvif.EncoderSnapshot"/>
    /// text), for «Restaurar»; null until a change is applied. Device state: not part of camera copies.
    /// </summary>
    public string? OriginalEncoders { get; set; }

    /// <summary>
    /// The stored password value (cameras.json) when this system could not read it — a DPAPI value from another PC,
    /// a keychain reference copied from a Mac… — so that saving writes it back unchanged instead of erasing it.
    /// Null when the password was read (or there is none). Cleared when the user types a new password.
    /// </summary>
    public string? UnreadablePassword { get; set; }

    /// <summary>The camera has a stored password this system cannot read: the user must type it again.</summary>
    public bool PasswordMissing => UnreadablePassword is not null;

    public static readonly int[] AllowedCooldowns = [30, 60, 300, 900];
    public static int NormalizeCooldown(int seconds) => AllowedCooldowns.Contains(seconds) ? seconds : 60;

    public Camera Clone() => (Camera)MemberwiseClone();

    /// <summary>A copy to add as a new camera: new Id, name with " (copia)", same address and credentials.</summary>
    public Camera Duplicate()
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{Name} (copia)";
        copy.UnreadablePassword = null;
        return copy;
    }
}
