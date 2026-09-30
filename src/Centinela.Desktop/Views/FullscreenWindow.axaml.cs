using Avalonia.Controls;
using Avalonia.Input;
using Centinela.Core;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

public partial class FullscreenWindow : Window
{
    readonly TileViewModel _tile = null!;

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
        var view = new CameraTileView(_tile);
        view.FullscreenRequested += _ => Close();
        Content = view;
        _tile.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    /// <summary>Stops the live session; the owner tracks it until exit.</summary>
    public Task ShutdownAsync() => _tile.ShutdownAsync();
}
