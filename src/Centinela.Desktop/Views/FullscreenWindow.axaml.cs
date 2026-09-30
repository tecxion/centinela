using Avalonia.Controls;
using Avalonia.Input;
using Centinela.Core;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

public partial class FullscreenWindow : Window
{
    readonly TileViewModel _tile = null!;
    readonly CameraTileView _view = null!;

    public FullscreenWindow() => InitializeComponent();

    public FullscreenWindow(Camera camera, bool showStats) : this()
    {
        Title = camera.Name;
        // Same stream as the camera's big view: a low-quality camera stays on its substream in fullscreen too.
        _tile = new TileViewModel(camera, ViewPlanner.StreamFor(camera, StreamKind.Main), manage: false, leases: null)
        {
            Big = true,
            ShowStats = showStats,
        };
        _view = new CameraTileView(_tile);
        _view.FullscreenRequested += _ => Close();
        Content = _view;
        _tile.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
        else if (e.Key is Key.D0 or Key.NumPad0) _view.ResetZoom();
    }

    /// <summary>Stops the live session; the owner tracks it until exit.</summary>
    public Task ShutdownAsync() => _tile.ShutdownAsync();
}
