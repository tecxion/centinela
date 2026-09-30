using Centinela.Core;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.ViewModels;
using Centinela.Desktop.Views;

namespace Centinela.Desktop.Services.Tiles;

/// <summary>Builds a tile (view model + view) for a camera and stream, started and ready to place.</summary>
sealed class TileFactory(StreamLeases leases, Func<bool> showStats)
{
    /// <summary>Raised for every new tile, before it starts, so the owner can wire its events.</summary>
    public event Action<CameraTileView>? Created;

    public CameraTileView Create(Camera camera, StreamKind stream, bool big)
    {
        var viewModel = new TileViewModel(camera, stream, manage: true, leases) { Big = big, ShowStats = showStats() };
        var view = new CameraTileView(viewModel);
        Created?.Invoke(view);
        viewModel.Start();
        return view;
    }
}
