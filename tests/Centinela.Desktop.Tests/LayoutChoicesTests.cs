using Centinela.Core;
using Centinela.Desktop.Services.Layout;

namespace Centinela.Desktop.Tests;

public class LayoutChoicesTests
{
    [Fact]
    public void Labels_match_the_windows_version()
    {
        Assert.Equal(["Auto", "1", "4", "9", "16", "Principal + miniaturas (derecha)",
            "Principal + miniaturas (izquierda)", "Dos principales + miniaturas"], LayoutChoices.Labels);
    }

    [Theory]
    [InlineData(0, LayoutMode.Grid, GridMode.Auto)]
    [InlineData(2, LayoutMode.Grid, GridMode.Four)]
    [InlineData(4, LayoutMode.Grid, GridMode.Sixteen)]
    [InlineData(5, LayoutMode.Featured, GridMode.Auto)]
    [InlineData(6, LayoutMode.FeaturedLeft, GridMode.Auto)]
    [InlineData(7, LayoutMode.Dual, GridMode.Auto)]
    public void Apply_and_IndexFor_round_trip(int index, LayoutMode layout, GridMode grid)
    {
        var settings = new AppSettings();
        LayoutChoices.Apply(settings, index);
        Assert.Equal(layout, settings.LayoutMode);
        if (layout == LayoutMode.Grid) Assert.Equal(grid, settings.GridMode);
        Assert.Equal(index, LayoutChoices.IndexFor(settings));
    }

    [Fact]
    public void Choosing_a_layout_keeps_the_grid_size_for_later()
    {
        var settings = new AppSettings { GridMode = GridMode.Nine };
        LayoutChoices.Apply(settings, 7);
        Assert.Equal(GridMode.Nine, settings.GridMode);
    }
}
