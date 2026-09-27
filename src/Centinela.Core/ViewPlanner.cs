namespace Centinela.Core;

public readonly record struct TileSlot(Guid CameraId, StreamKind Kind, int Row, int Column, int RowSpan, int ColumnSpan);

public sealed record ViewPlan(int Rows, int Columns, IReadOnlyList<TileSlot> Slots);

public static class ViewPlanner
{
    public static ViewPlan Plan(IReadOnlyList<Camera> cameras, LayoutMode mode, GridMode gridMode, Guid? featuredCameraId)
    {
        var ordered = cameras.OrderBy(c => c.Order).ToList();
        if (ordered.Count == 0) return new ViewPlan(1, 1, []);

        if (mode == LayoutMode.Featured)
        {
            var featured = ordered.FirstOrDefault(c => c.Id == featuredCameraId) ?? ordered[0];
            var layout = GridLayout.ComputeFeatured(ordered.Count);
            var slots = new List<TileSlot>
            {
                new(featured.Id, StreamKind.Main, 0, 0, layout.FeaturedSpan, layout.FeaturedSpan),
            };
            slots.AddRange(ordered.Where(c => c.Id != featured.Id).Select((c, i) =>
                new TileSlot(c.Id, StreamKind.Sub, layout.Slots[i].Row, layout.Slots[i].Column, 1, 1)));
            return new ViewPlan(layout.Size, layout.Size, slots);
        }

        var visible = ordered.Take(GridLayout.VisibleCount(ordered.Count, gridMode)).ToList();
        var size = GridLayout.Compute(visible.Count, gridMode);
        return new ViewPlan(size.Rows, size.Columns, visible
            .Select((c, i) => new TileSlot(c.Id, StreamKind.Sub, i / size.Columns, i % size.Columns, 1, 1))
            .ToList());
    }
}
