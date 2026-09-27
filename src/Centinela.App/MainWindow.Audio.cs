using Centinela.Core;

namespace Centinela.App;

/// <summary>Audio: at most one tile (big or fullscreen) plays, through one shared WASAPI output.</summary>
public partial class MainWindow
{
    const string NoAudioOutput = "No hay salida de audio";
    readonly AudioCoordinator<CameraTile> _audio = new();
    // Created on the first 🔊 click; dropped when the device fails so the next click opens the current device.
    AudioOutput? _audioOutput;

    void WireAudio(CameraTile tile)
    {
        tile.AudioToggleRequested += ToggleAudio;
        tile.AudioLost += ReleaseAudio;
        tile.AudioFailed += (t, message) =>
        {
            ReleaseAudio(t);
            ReportAudioFailure(t, message);
        };
    }

    void ToggleAudio(CameraTile tile)
    {
        if (tile.AudioOn)
        {
            ReleaseAudio(tile);
            return;
        }
        if (_audioOutput is null && AudioOutput.TryCreate() is { } created)
        {
            created.Failed += (output, message) => Dispatcher.BeginInvoke(() => OnAudioOutputFailed(output, message));
            _audioOutput = created;
        }
        if (_audioOutput is null)
        {
            // Not latched: tiles created later (layout change, fullscreen, back from the tray) try again.
            foreach (var wired in AudioTiles()) wired.DisableAudio(NoAudioOutput);
            Notify("No hay salida de audio en este equipo.", null);
            return;
        }
        _audio.Activate(tile)?.SetAudio(null);
        _audioOutput.Clear();
        tile.SetAudio(_audioOutput);
    }

    /// <summary>Silences the tile if it is the one playing (tile removed, fullscreen closed, failure).</summary>
    void ReleaseAudio(CameraTile tile)
    {
        tile.SetAudio(null);
        if (_audio.Deactivate(tile)) _audioOutput?.Clear();
    }

    /// <summary>The device went away: silence the playing camera, report it, and reopen a device on the next click.</summary>
    void OnAudioOutputFailed(AudioOutput output, string message)
    {
        if (!ReferenceEquals(output, _audioOutput)) return; // already replaced or disposed
        _audioOutput = null;
        var playing = _audio.Active;
        if (playing is not null)
        {
            ReleaseAudio(playing);
            ReportAudioFailure(playing, message);
        }
        output.Dispose();
    }

    void ReportAudioFailure(CameraTile tile, string message)
    {
        var title = $"No se pudo reproducir el audio de «{tile.Camera.Name}»";
        _errorLog.Add(new ErrorLogEntry(DateTime.Now, tile.Camera.Name, "Audio", title, message));
        ShowToast(new Toast(title, "El vídeo sigue funcionando.", ToastStyle.Error));
    }

    /// <summary>Every tile that can show the audio button: big grid tiles and fullscreen views.</summary>
    IEnumerable<CameraTile> AudioTiles() =>
        _tiles.Values.Concat(OwnedWindows.OfType<FullscreenWindow>().Select(w => w.Tile)).Where(t => t.AudioCapable);
}
