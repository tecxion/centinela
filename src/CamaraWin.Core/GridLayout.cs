namespace CamaraWin.Core;

public readonly record struct GridSize(int Rows, int Columns);

public sealed record FeaturedLayout(int Size, int FeaturedSpan, IReadOnlyList<(int Row, int Column)> Slots);

public static class GridLayout
{
    public static int VisibleCount(int cameraCount, GridMode mode) =>
        mode == GridMode.Auto ? cameraCount : Math.Min(cameraCount, (int)mode);

    public static GridSize Compute(int cameraCount, GridMode mode)
    {
        if (mode != GridMode.Auto)
        {
            var side = (int)Math.Sqrt((int)mode);
            return new GridSize(side, side);
        }
        if (cameraCount <= 1) return new GridSize(1, 1);
        var columns = (int)Math.Ceiling(Math.Sqrt(cameraCount));
        var rows = (int)Math.Ceiling(cameraCount / (double)columns);
        return new GridSize(rows, columns);
    }

    /// <summary>
    /// Featured camera spans k×k cells of a (k+1)×(k+1) grid; the others fill the right column
    /// (top→bottom) then the bottom row (left→right). k = max(1, ceil((n−2)/2)).
    /// </summary>
    public static FeaturedLayout ComputeFeatured(int cameraCount)
    {
        if (cameraCount <= 1) return new FeaturedLayout(1, 1, []);
        var k = Math.Max(1, (int)Math.Ceiling((cameraCount - 2) / 2.0));
        var slots = new List<(int Row, int Column)>();
        for (var row = 0; row < k; row++) slots.Add((row, k));
        for (var column = 0; column <= k; column++) slots.Add((k, column));
        return new FeaturedLayout(k + 1, k, slots.Take(cameraCount - 1).ToList());
    }
}
