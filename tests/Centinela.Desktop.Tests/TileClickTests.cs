using Avalonia;
using Centinela.Desktop.Views;

namespace Centinela.Desktop.Tests;

public class TileClickTests
{
    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(199, 99, true)]
    [InlineData(-1, 50, false)]
    [InlineData(50, -1, false)]
    [InlineData(201, 50, false)]
    [InlineData(50, 101, false)]
    public void Only_a_release_inside_the_tile_is_a_click(double x, double y, bool expected) =>
        Assert.Equal(expected, TileClick.ReleasedInside(new Size(200, 100), new Point(x, y)));
}
