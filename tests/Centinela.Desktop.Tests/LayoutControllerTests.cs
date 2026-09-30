using Centinela.Core;
using Centinela.Desktop.Services.Layout;

namespace Centinela.Desktop.Tests;

public class LayoutControllerTests
{
    static List<Camera> Cameras(int n) =>
        Enumerable.Range(0, n).Select(i => new Camera { Name = $"C{i}", Host = $"h{i}", Order = i }).ToList();

    [Fact]
    public void Grid_uses_substreams_and_no_big_tile()
    {
        var plan = new LayoutController(Cameras(4), new AppSettings()).Plan();
        Assert.Equal((2, 2), (plan.Rows, plan.Columns));
        Assert.All(plan.Tiles, t => Assert.Equal((StreamKind.Sub, false), (t.Stream, t.Big)));
    }

    [Fact]
    public void Featured_tile_is_big_on_the_main_stream_unless_low_quality()
    {
        var cams = Cameras(5);
        var settings = new AppSettings { LayoutMode = LayoutMode.Featured, FeaturedCameraId = cams[2].Id };
        var big = new LayoutController(cams, settings).Plan().Tiles.Single(t => t.Big);
        Assert.Equal((cams[2].Id, StreamKind.Main), (big.CameraId, big.Stream));

        cams[2].LowQualityWhenBig = true;
        big = new LayoutController(cams, settings).Plan().Tiles.Single(t => t.Big);
        Assert.Equal((cams[2].Id, StreamKind.Sub), (big.CameraId, big.Stream));
    }

    [Fact]
    public void Click_features_a_thumbnail_and_ignores_the_featured_one()
    {
        var cams = Cameras(4);
        var settings = new AppSettings { LayoutMode = LayoutMode.Featured };
        var layout = new LayoutController(cams, settings);
        Assert.False(layout.Click(cams[0].Id)); // first camera is featured by default
        Assert.True(layout.Click(cams[3].Id));
        Assert.Equal(cams[3].Id, settings.FeaturedCameraId);
    }

    [Fact]
    public void Click_in_dual_replaces_the_older_big_camera()
    {
        var cams = Cameras(5);
        var settings = new AppSettings { LayoutMode = LayoutMode.Dual };
        var layout = new LayoutController(cams, settings);
        Assert.True(layout.Click(cams[4].Id));
        Assert.Contains(cams[4].Id, layout.BigCameraIds());
        Assert.Equal(2, layout.BigCameraIds().Count);
    }

    [Fact]
    public void Click_in_the_grid_does_nothing()
    {
        var cams = Cameras(4);
        Assert.False(new LayoutController(cams, new AppSettings()).Click(cams[1].Id));
    }

    [Fact]
    public void Drop_on_another_tile_swaps_the_order()
    {
        var cams = Cameras(3);
        var layout = new LayoutController(cams, new AppSettings());
        Assert.Equal(DropOutcome.OrderChanged, layout.Drop(cams[0].Id, cams[2].Id));
        Assert.Equal((2, 0), (cams[0].Order, cams[2].Order));
    }

    [Fact]
    public void Drop_on_the_featured_tile_features_the_dragged_camera()
    {
        var cams = Cameras(3);
        var settings = new AppSettings { LayoutMode = LayoutMode.Featured };
        var layout = new LayoutController(cams, settings);
        Assert.Equal(DropOutcome.SettingsChanged, layout.Drop(cams[2].Id, cams[0].Id));
        Assert.Equal(cams[2].Id, settings.FeaturedCameraId);
        Assert.Equal([0, 1, 2], cams.Select(c => c.Order));
    }

    [Fact]
    public void Drop_on_a_big_dual_tile_puts_the_dragged_camera_there()
    {
        var cams = Cameras(4);
        var settings = new AppSettings { LayoutMode = LayoutMode.Dual };
        var layout = new LayoutController(cams, settings);
        var target = layout.BigCameraIds()[1];
        Assert.Equal(DropOutcome.SettingsChanged, layout.Drop(cams[3].Id, target));
        Assert.Equal(cams[3].Id, layout.BigCameraIds()[1]);
    }

    [Fact]
    public void Drop_with_an_unknown_camera_does_nothing()
    {
        var cams = Cameras(2);
        Assert.Equal(DropOutcome.None, new LayoutController(cams, new AppSettings()).Drop(Guid.NewGuid(), cams[0].Id));
    }
}
