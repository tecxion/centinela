using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>What the tile's buttons, menu and indicators show; set by the owner (texts as in the Windows version).</summary>
sealed partial class TileViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityText), nameof(QualityToolTip))]
    private bool _lowQuality;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotionToolTip))]
    private bool _motionEnabled = true;

    [ObservableProperty] private bool _smoothingOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordText), nameof(RecordToolTip), nameof(RecDotVisible), nameof(RecDotHollow),
        nameof(RecDotToolTip))]
    private RecordingStatus _recording;

    [ObservableProperty] private bool _showFixButton;

    /// <summary>Set by the owner for big (Main) tiles and fullscreen; the button shows only if the stream has audio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioButtonVisible))]
    private bool _audioCapable;

    /// <summary>The current connection has an audio track (from the session's stream info).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioButtonVisible))]
    private bool _hasAudio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioText))]
    private bool _audioOn;

    [ObservableProperty] private bool _audioEnabled = true;
    [ObservableProperty] private string _audioToolTip = "Activar sonido";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSnapshot))]
    private bool _snapshotBusy;

    /// <summary>Set by the zoom support while the image is zoomed in, with its scale label.</summary>
    [ObservableProperty] private bool _isZoomed;
    [ObservableProperty] private string _zoomText = "";

    /// <summary>
    /// Wheel zoom and drag-to-pan: follows <see cref="Big"/> (big tiles and fullscreen); the owner turns it off for a
    /// tile that only covers its replacement. Turning it off resets the zoom (the view does).
    /// </summary>
    [ObservableProperty] private bool _enableZoom;

    /// <summary>Raised on the UI thread when a new connection has no audio track while this tile's audio is on.</summary>
    public event Action<TileViewModel>? AudioLost;

    public string QualityText => LowQuality ? "SD" : "HD";

    public string QualityToolTip => LowQuality
        ? "Calidad baja (flujo secundario): más fluida con mala conexión. Pulsa para calidad alta."
        : "Calidad alta (flujo principal). Pulsa para calidad baja si la imagen va a saltos.";

    /// <summary>The HD/SD button: big tiles that can be managed only.</summary>
    public bool QualityButtonVisible => Big && Manage;

    public string MotionToolTip => MotionEnabled ? "Detección de movimiento: activada" : "Detección de movimiento: desactivada";

    public string RecordText => Recording == RecordingStatus.Off ? "⏺" : "⏹";
    public string RecordToolTip => Recording == RecordingStatus.Off ? "Grabar" : "Detener grabación";
    public bool RecDotVisible => Recording != RecordingStatus.Off;
    /// <summary>Solid red while recording; hollow while the recording session (re)connects.</summary>
    public bool RecDotHollow => Recording is RecordingStatus.Connecting or RecordingStatus.Paused;

    public string RecDotToolTip => Recording switch
    {
        RecordingStatus.Off => "",
        RecordingStatus.Recording => "Grabando",
        RecordingStatus.Paused => "Grabación en pausa: reconectando…",
        _ => "Grabación: conectando…",
    };

    public bool AudioButtonVisible => AudioCapable && HasAudio;
    public string AudioText => AudioOn ? "🔊" : "🔇";

    /// <summary>False when the camera has no usable URL: nothing to capture or record.</summary>
    public bool HasSession => Session is not null;
    public bool CanSnapshot => HasSession && !SnapshotBusy;

    partial void OnBigChanged(bool value)
    {
        OnPropertyChanged(nameof(QualityButtonVisible));
        EnableZoom = value;
    }

    /// <summary>The zoom label and the «Restablecer zoom» item, from the zoom support.</summary>
    public void ShowZoom(bool zoomed, double scale)
    {
        IsZoomed = zoomed;
        ZoomText = string.Format(Spanish, "{0:0.#}×", scale);
    }

    /// <summary>Shows which stream the camera uses when big: HD (main) or SD (substream).</summary>
    public void SetLowQuality(bool low) => LowQuality = low;

    /// <summary>Shows the recording owned by the recording controller (dot and button).</summary>
    public void SetRecordingStatus(RecordingStatus status) => Recording = status;

    /// <summary>Shows whether detection is on: button tooltip, menu check and the crossed-out eye by the name.</summary>
    public void SetMotionEnabled(bool enabled) => MotionEnabled = enabled;

    /// <summary>The camera's smoothing setting: menu check, and the own session (shared ones are set by the owner).</summary>
    public void SetSmoothing(bool enabled)
    {
        SmoothingOn = enabled;
        if (_lease is null && Session is not null && !_disposed) Session.Smoothing = enabled;
    }

    /// <summary>Audio button and indicator for audio on or off. Shared sessions never play audio.</summary>
    internal void ApplyAudioOn(bool on)
    {
        AudioOn = on && !_disposed && _lease is null;
        // A disabled button keeps the tooltip that explains why.
        if (AudioEnabled) AudioToolTip = AudioOn ? "Silenciar" : "Activar sonido";
    }

    public void DisableAudio(string reason)
    {
        AudioEnabled = false;
        AudioToolTip = reason;
    }

    /// <summary>Reads the session's audio track; UI thread.</summary>
    void UpdateAudio()
    {
        if (_disposed) return;
        HasAudio = Session?.Info?.AudioCodec is not null;
        // Reconnected to a stream without audio: nothing will sound, so the owner returns the tile to muted.
        if (AudioOn && !HasAudio) AudioLost?.Invoke(this);
    }
}
