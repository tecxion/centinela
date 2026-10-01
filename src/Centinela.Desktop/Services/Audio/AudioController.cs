using Centinela.Core;
using Centinela.Desktop.Services.Ui;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Services.Audio;

/// <summary>
/// Audio: at most one tile (big or fullscreen) plays, through one shared OpenAL output that is opened on a 🔊 click
/// and closed when nothing plays (so an idle app holds no audio device) or when the device fails, so the next click
/// opens the current device. UI thread only.
/// </summary>
/// <param name="audioTiles">Every tile that can show the audio button: big grid tiles and fullscreen views.</param>
sealed class AudioController(Func<IEnumerable<TileViewModel>> audioTiles) : IDisposable
{
    const string NoAudioOutput = "No hay salida de audio";
    readonly AudioCoordinator<TileViewModel> _audio = new();
    OpenAlAudioOutput? _output;
    bool _closed;

    /// <summary>A message for the status line.</summary>
    public event Action<string>? Notice;

    /// <summary>A camera's audio could not be played (for the error log): the camera, the title and the detail.</summary>
    public event Action<Camera, string, string>? Failed;

    /// <summary>A new tile: silenced when its stream loses the audio track or its audio cannot be played.</summary>
    public void Attach(TileViewModel tile)
    {
        tile.AudioLost += Release;
        tile.AudioFailed += (t, message) =>
        {
            Release(t);
            ReportFailure(t, message);
        };
    }

    /// <summary>The tile's 🔊 click; ignored for a closed tile or one that cannot play audio.</summary>
    public void Toggle(TileViewModel tile)
    {
        if (_closed || tile.IsShutDown || !tile.AudioCapable) return;
        if (tile.AudioOn)
        {
            Release(tile);
            return;
        }
        if (_output is null && OpenAlAudioOutput.TryCreate() is { } created)
        {
            created.Failed += (output, message) => UiThread.Post(() => OnOutputFailed(output, message));
            _output = created;
        }
        if (_output is null)
        {
            // Not latched: tiles created later (layout change, fullscreen) try again.
            foreach (var capable in audioTiles().Where(t => t.AudioCapable)) capable.DisableAudio(NoAudioOutput);
            Notice?.Invoke("No hay salida de audio en este equipo.");
            return;
        }
        _audio.Activate(tile)?.SetAudio(null);
        _output.Clear();
        tile.SetAudio(_output);
    }

    /// <summary>
    /// Silences the tile if it is the one playing (tile removed, no longer big, covering its replacement, fullscreen
    /// closed, failure) and closes the output once nothing plays.
    /// </summary>
    public void Release(TileViewModel tile)
    {
        tile.SetAudio(null);
        if (_audio.Deactivate(tile)) CloseOutput();
    }

    /// <summary>Hidden in the tray: silences whatever plays and closes the output (an idle app holds no audio device).</summary>
    public void ReleaseAll()
    {
        if (_audio.Active is { } playing) Release(playing);
        CloseOutput();
    }

    /// <summary>
    /// A pump may still be finishing a Write after SetAudio(null): the disposed output ignores it, and its Failed
    /// event is ignored too (no longer <see cref="_output"/>).
    /// </summary>
    void CloseOutput()
    {
        var output = _output;
        _output = null;
        output?.Dispose();
    }

    /// <summary>The device went away: silence the playing camera, report it, and reopen a device on the next click.</summary>
    void OnOutputFailed(OpenAlAudioOutput output, string message)
    {
        if (_closed || !ReferenceEquals(output, _output)) return; // already replaced or disposed
        _output = null;
        if (_audio.Active is { } playing)
        {
            Release(playing);
            ReportFailure(playing, message);
        }
        output.Dispose();
    }

    /// <summary>The status line shows the title and «El vídeo sigue funcionando.»; the error log gets the detail.</summary>
    void ReportFailure(TileViewModel tile, string message)
    {
        var title = $"No se pudo reproducir el audio de «{tile.Camera.Name}»";
        Notice?.Invoke($"{title}. El vídeo sigue funcionando.");
        Failed?.Invoke(tile.Camera, title, message);
    }

    /// <summary>Window closed: closes the output; later calls do nothing.</summary>
    public void Dispose()
    {
        _closed = true;
        CloseOutput();
    }
}
