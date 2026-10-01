using Centinela.Core;
using Centinela.Desktop.Services.Audio;
using Centinela.Desktop.Services.Notices;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

/// <summary>Audio: at most one tile (big or fullscreen) plays; the controller owns the shared output.</summary>
public partial class MainWindow
{
    AudioController _audio = null!;

    void InitAudio()
    {
        _audio = new AudioController(AudioTiles);
        _audio.Notice += message => _vm.Notify(message, null);
        // Logged, and an error notice (a system notice while hidden in the tray), as in the Windows version.
        _audio.Failed += (camera, title, detail) =>
        {
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Audio", title, detail));
            if (!_closing) _notices.Show(new Toast(title, "El vídeo sigue funcionando.", ToastStyle.Error));
        };
        _tiles.AudioReleaseRequested += _audio.Release;
    }

    /// <summary>A new tile (grid or fullscreen): its 🔊 button and the failures that silence it.</summary>
    void WireAudio(CameraTileView tile)
    {
        tile.AudioToggleRequested += t => _audio.Toggle(t.ViewModel);
        _audio.Attach(tile.ViewModel);
    }

    /// <summary>Every tile that may show the audio button: grid tiles (and placeholders) plus fullscreen views.</summary>
    IEnumerable<TileViewModel> AudioTiles() => AllTiles().Select(t => t.ViewModel);
}
