using System.Windows;
using System.Windows.Input;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class FullscreenWindow : Window
{
    readonly CameraTile _tile;

    public FullscreenWindow(Camera camera)
    {
        InitializeComponent();
        Title = camera.Name;
        _tile = new CameraTile(camera, StreamKind.Main, manage: false);
        _tile.FullscreenRequested += _ => Close();
        Content = _tile;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        // Non-blocking: the live session stops in the background.
        Closed += (_, _) => _tile.Dispose();
    }

    /// <summary>The window's tile; the owner wires its record and notify events like a grid tile.</summary>
    internal CameraTile Tile => _tile;
}
