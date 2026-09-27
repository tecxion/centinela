using Centinela.Core;

namespace Centinela.App;

/// <summary>Audio: at most one tile (big or fullscreen) plays, through one shared WASAPI output.</summary>
public partial class MainWindow
{
    readonly AudioCoordinator<CameraTile> _audio = new();
    AudioOutput? _audioOutput;
    bool _audioUnavailable;

    void WireAudio(CameraTile tile)
    {
        tile.AudioToggleRequested += ToggleAudio;
        tile.AudioFailed += (t, message) =>
        {
            ReleaseAudio(t);
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, t.Camera.Name, "Audio", $"No se pudo reproducir el audio de «{t.Camera.Name}»", message));
            ShowToast(new Toast($"No se pudo reproducir el audio de «{t.Camera.Name}»", "El vídeo sigue funcionando.", ToastStyle.Error));
        };
        if (_audioUnavailable) tile.DisableAudio("No hay salida de audio");
    }

    void ToggleAudio(CameraTile tile)
    {
        if (tile.AudioOn)
        {
            ReleaseAudio(tile);
            return;
        }
        _audioOutput ??= AudioOutput.TryCreate();
        if (_audioOutput is null)
        {
            _audioUnavailable = true;
            tile.DisableAudio("No hay salida de audio");
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
}
