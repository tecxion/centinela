using Centinela.Desktop.Services.Audio;
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
        _tiles.AudioReleaseRequested += _audio.Release;
    }

    /// <summary>A new tile (grid or fullscreen): its 🔊 button and the failures that silence it.</summary>
    void WireAudio(CameraTileView tile)
    {
        tile.AudioToggleRequested += t => _audio.Toggle(t.ViewModel);
        _audio.Attach(tile.ViewModel);
    }

    /// <summary>Every tile that may show the audio button: grid tiles (and placeholders) plus fullscreen views.</summary>
    IEnumerable<TileViewModel> AudioTiles() =>
        _tiles.All.Concat(OwnedWindows.OfType<FullscreenWindow>().Select(w => w.Tile)).Select(t => t.ViewModel);
}
