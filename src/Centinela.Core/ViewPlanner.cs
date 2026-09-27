namespace Centinela.Core;

public readonly record struct TileSlot(Guid CameraId, StreamKind Kind, int Row, int Column, int RowSpan, int ColumnSpan);

public sealed record ViewPlan(int Rows, int Columns, IReadOnlyList<TileSlot> Slots);

public static class ViewPlanner
{
    public static ViewPlan Plan(IReadOnlyList<Camera> cameras, LayoutMode mode, GridMode gridMode, Guid? featuredCameraId,
        IReadOnlyList<Guid>? dualCameraIds = null)
    {
        var ordered = cameras.OrderBy(c => c.Order).ToList();
        if (ordered.Count == 0) return new ViewPlan(1, 1, []);

        if (mode is LayoutMode.Featured or LayoutMode.FeaturedLeft)
        {
            var featured = ordered.FirstOrDefault(c => c.Id == featuredCameraId) ?? ordered[0];
            var layout = GridLayout.ComputeFeatured(ordered.Count, mirrored: mode == LayoutMode.FeaturedLeft);
            var slots = new List<TileSlot>
            {
                new(featured.Id, StreamKind.Main, 0, layout.FeaturedColumn, layout.FeaturedSpan, layout.FeaturedSpan),
            };
            slots.AddRange(ordered.Where(c => c.Id != featured.Id).Select((c, i) =>
                new TileSlot(c.Id, StreamKind.Sub, layout.Slots[i].Row, layout.Slots[i].Column, 1, 1)));
            return new ViewPlan(layout.Size, layout.Size, slots);
        }

        if (mode == LayoutMode.Dual)
        {
            var layout = GridLayout.ComputeDual(ordered.Count);
            var known = ordered.Select(c => c.Id).ToHashSet();
            var bigs = (dualCameraIds ?? []).Where(known.Contains).Distinct().Take(2).ToList();
            foreach (var camera in ordered)
            {
                if (bigs.Count >= layout.Bigs.Count) break;
                if (!bigs.Contains(camera.Id)) bigs.Add(camera.Id);
            }
            var slots = bigs.Select((id, i) =>
                new TileSlot(id, StreamKind.Main, layout.Bigs[i].Row, layout.Bigs[i].Column, layout.BigSpan, layout.BigSpan)).ToList();
            slots.AddRange(ordered.Where(c => !bigs.Contains(c.Id)).Select((c, i) =>
                new TileSlot(c.Id, StreamKind.Sub, layout.Slots[i].Row, layout.Slots[i].Column, 1, 1)));
            return new ViewPlan(layout.Rows, layout.Columns, slots);
        }

        var visible = ordered.Take(GridLayout.VisibleCount(ordered.Count, gridMode)).ToList();
        var size = GridLayout.Compute(visible.Count, gridMode);
        return new ViewPlan(size.Rows, size.Columns, visible
            .Select((c, i) => new TileSlot(c.Id, StreamKind.Sub, i / size.Columns, i % size.Columns, 1, 1))
            .ToList());
    }
}
