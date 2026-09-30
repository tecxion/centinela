using Avalonia;

namespace Centinela.Desktop.Views;

/// <summary>When a press and release on a tile count as a click.</summary>
static class TileClick
{
    /// <summary>
    /// A button released outside the tile is not a click (the pointer may be captured, so the release still reaches
    /// the tile). <paramref name="release"/> is relative to the tile, hence the tile's own size, not its Bounds.
    /// </summary>
    public static bool ReleasedInside(Size tile, Point release) => new Rect(tile).Contains(release);
}
