using Centinela.Desktop.Services.Ui;
using Centinela.Media;

namespace Centinela.Desktop.ViewModels;

/// <summary>Audio of the tile: the owner (AudioController) turns it on and off; shared sessions never play.</summary>
sealed partial class TileViewModel
{
    /// <summary>Raised on the UI thread when a new connection has no audio track while this tile's audio is on.</summary>
    public event Action<TileViewModel>? AudioLost;

    /// <summary>Raised on the UI thread when the audio of this tile, while on, cannot be played.</summary>
    public event Action<TileViewModel, string>? AudioFailed;

    /// <summary>Plays this tile's audio into <paramref name="sink"/>, or silences it with null.</summary>
    public void SetAudio(IAudioSink? sink)
    {
        // Shared sessions never play audio (Sub tiles are not audio-capable): the sink would reach every viewer.
        var on = sink is not null && !_disposed && _lease is null;
        if (_lease is null && !_disposed) Session?.SetAudioSink(on ? sink : null);
        ApplyAudioOn(on);
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

    /// <summary>From the session's audio thread: reported only while this tile still plays.</summary>
    void OnAudioFailed(string message) => UiThread.Post(() =>
    {
        if (!_disposed && AudioOn) AudioFailed?.Invoke(this, message);
    });
}
