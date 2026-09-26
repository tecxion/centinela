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
        _tile.Notify += (message, path) => (Owner as MainWindow)?.Notify(message, path);
        Content = _tile;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        // Non-blocking: the session stops and any recording finalizes in the background.
        Closed += (_, _) => _tile.Dispose();
    }

    internal bool TileIsRecording => _tile.IsRecording;

    /// <summary>Completes once the tile's session (and any recording) has shut down after the window closed.</summary>
    internal Task TileShutdown => _tile.ShutdownAsync();
}
