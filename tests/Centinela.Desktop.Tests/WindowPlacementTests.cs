using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Tests;

public class WindowPlacementTests
{
    static readonly (int, int, int, int)[] TwoScreens = [(0, 0, 1920, 1080), (1920, 0, 1920, 1080)];

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(2500, 300, true)]      // second screen
    [InlineData(3800, 300, false)]     // less than 100 px left on the right
    [InlineData(-50, 100, false)]
    [InlineData(100, 1000, false)]     // less than 100 px left at the bottom
    public void A_saved_corner_must_leave_the_window_reachable(double left, double top, bool expected) =>
        Assert.Equal(expected, WindowPlacement.IsVisibleOn(TwoScreens, left, top));
}
