namespace Centinela.Core;

public readonly record struct GridSize(int Rows, int Columns);

public sealed record FeaturedLayout(int Size, int FeaturedSpan, int FeaturedColumn, IReadOnlyList<(int Row, int Column)> Slots);

public sealed record DualLayout(int Rows, int Columns, int BigSpan,
    IReadOnlyList<(int Row, int Column)> Bigs, IReadOnlyList<(int Row, int Column)> Slots);

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
    /// Featured camera spans k×k cells of a (k+1)×(k+1) grid, k = max(1, ceil((n−2)/2)). The others fill the free
    /// column (right, or left when <paramref name="mirrored"/>) top→bottom, then the bottom row left→right.
    /// </summary>
    public static FeaturedLayout ComputeFeatured(int cameraCount, bool mirrored = false)
    {
        if (cameraCount <= 1) return new FeaturedLayout(1, 1, 0, []);
        var k = Math.Max(1, (int)Math.Ceiling((cameraCount - 2) / 2.0));
        var column = mirrored ? 0 : k;
        var slots = new List<(int Row, int Column)>();
        for (var row = 0; row < k; row++) slots.Add((row, column));
        for (var c = 0; c <= k; c++) slots.Add((k, c));
        return new FeaturedLayout(k + 1, k, mirrored ? 1 : 0, slots.Take(cameraCount - 1).ToList());
    }

    /// <summary>
    /// Two big cameras side by side on top (each BigSpan×BigSpan), thumbnails below in rows of W = 2·BigSpan.
    /// Up to 6 thumbnails fit one row; more use two. W is even and at least 4.
    /// </summary>
    public static DualLayout ComputeDual(int cameraCount)
    {
        if (cameraCount <= 0) return new DualLayout(1, 1, 1, [], []);
        if (cameraCount == 1) return new DualLayout(1, 1, 1, [(0, 0)], []);
        if (cameraCount == 2) return new DualLayout(1, 2, 1, [(0, 0), (0, 1)], []);
        var thumbnails = cameraCount - 2;
        var perRow = thumbnails <= 6 ? thumbnails : (int)Math.Ceiling(thumbnails / 2.0);
        var width = Math.Max(4, perRow + perRow % 2);
        var rows = (int)Math.Ceiling(thumbnails / (double)width);
        var span = width / 2;
        var slots = Enumerable.Range(0, thumbnails).Select(i => (span + i / width, i % width)).ToList();
        return new DualLayout(span + rows, width, span, [(0, 0), (0, span)], slots);
    }
}
