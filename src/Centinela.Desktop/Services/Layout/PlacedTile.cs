using Centinela.Core;

namespace Centinela.Desktop.Services.Layout;

/// <param name="Stream">The stream the tile plays (a big low-quality camera plays its substream).</param>
/// <param name="Big">A big tile of the featured/dual layouts: zoom, large buttons, quality button.</param>
readonly record struct PlacedTile(Guid CameraId, StreamKind Stream, bool Big, int Row, int Column, int RowSpan, int ColumnSpan);

sealed record LayoutPlan(int Rows, int Columns, IReadOnlyList<PlacedTile> Tiles);

enum DropOutcome { None, SettingsChanged, OrderChanged }
