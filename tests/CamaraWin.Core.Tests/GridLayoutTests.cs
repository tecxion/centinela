using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class GridLayoutTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(7, 3, 3)]
    [InlineData(10, 3, 4)]
    public void Auto_layout(int count, int rows, int cols) =>
        Assert.Equal(new GridSize(rows, cols), GridLayout.Compute(count, GridMode.Auto));

    [Theory]
    [InlineData(GridMode.One, 1)]
    [InlineData(GridMode.Four, 2)]
    [InlineData(GridMode.Nine, 3)]
    [InlineData(GridMode.Sixteen, 4)]
    public void Fixed_layout_is_square_regardless_of_count(GridMode mode, int side) =>
        Assert.Equal(new GridSize(side, side), GridLayout.Compute(7, mode));

    [Theory]
    [InlineData(7, GridMode.Auto, 7)]
    [InlineData(7, GridMode.Four, 4)]
    [InlineData(3, GridMode.Nine, 3)]
    [InlineData(7, GridMode.One, 1)]
    public void Visible_count(int count, GridMode mode, int expected) =>
        Assert.Equal(expected, GridLayout.VisibleCount(count, mode));
}
