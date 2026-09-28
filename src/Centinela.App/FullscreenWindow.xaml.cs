using System.Windows;
using System.Windows.Input;
using Centinela.Core;

namespace Centinela.App;

public partial class FullscreenWindow : Window
{
    readonly CameraTile _tile;

    public FullscreenWindow(Camera camera)
    {
        InitializeComponent();
        Title = camera.Name;
        // Same stream as the camera's big view: a low-quality camera stays on its substream in fullscreen too.
        _tile = new CameraTile(camera, ViewPlanner.StreamFor(camera, StreamKind.Main), manage: false) { EnableZoom = true, Big = true };
        _tile.FullscreenRequested += _ => Close();
        Content = _tile;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.D0 or Key.NumPad0) _tile.ResetZoom();
        };
        // Non-blocking: the live session stops in the background.
        Closed += (_, _) => _tile.Dispose();
    }

    /// <summary>The window's tile; the owner wires its record and notify events like a grid tile.</summary>
    internal CameraTile Tile => _tile;
}
