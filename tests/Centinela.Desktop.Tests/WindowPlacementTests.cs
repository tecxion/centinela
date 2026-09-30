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

    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.5, 150)]
    [InlineData(-640, 1.25, -800)]
    [InlineData(333.4, 1.5, 500)]     // rounds to the nearest pixel
    public void Saved_dips_become_screen_pixels(double dip, double scale, int expected) =>
        Assert.Equal(expected, WindowPlacement.ToPixels(dip, scale));

    [Theory]
    [InlineData(100, 1.0, 100)]
    [InlineData(150, 1.5, 100)]
    [InlineData(-800, 1.25, -640)]
    public void Screen_pixels_are_saved_as_dips_like_wpf(int px, double scale, double expected) =>
        Assert.Equal(expected, WindowPlacement.ToDips(px, scale), 6);
}
