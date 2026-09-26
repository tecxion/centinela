namespace CamaraWin.Core;

public readonly record struct GridSize(int Rows, int Columns);

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
}
