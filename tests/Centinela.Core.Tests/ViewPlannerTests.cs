using Centinela.Core;

namespace Centinela.Core.Tests;

public class ViewPlannerTests
{
    static List<Camera> Cameras(int n) =>
        Enumerable.Range(0, n).Select(i => new Camera { Name = $"C{i}", Order = n - 1 - i }).ToList();

    [Fact]
    public void Empty_list_has_no_slots() =>
        Assert.Empty(ViewPlanner.Plan([], LayoutMode.Featured, GridMode.Auto, null).Slots);

    [Fact]
    public void Grid_mode_places_substreams_row_major_by_order()
    {
        var cams = Cameras(7);
        var plan = ViewPlanner.Plan(cams, LayoutMode.Grid, GridMode.Auto, null);
        Assert.Equal((3, 3), (plan.Rows, plan.Columns));
        var byOrder = cams.OrderBy(c => c.Order).ToList();
        for (var i = 0; i < 7; i++)
            Assert.Equal(new TileSlot(byOrder[i].Id, StreamKind.Sub, i / 3, i % 3, 1, 1), plan.Slots[i]);
    }

    [Fact]
    public void Grid_fixed_mode_hides_extra_cameras() =>
        Assert.Equal(4, ViewPlanner.Plan(Cameras(7), LayoutMode.Grid, GridMode.Four, null).Slots.Count);

    [Fact]
    public void Featured_defaults_to_first_by_order_on_main_stream()
    {
        var cams = Cameras(7);
        var first = cams.OrderBy(c => c.Order).First();
        var plan = ViewPlanner.Plan(cams, LayoutMode.Featured, GridMode.Auto, null);
        Assert.Equal((4, 4), (plan.Rows, plan.Columns));
        Assert.Equal(new TileSlot(first.Id, StreamKind.Main, 0, 0, 3, 3), plan.Slots[0]);
        Assert.Equal(7, plan.Slots.Count);
        Assert.All(plan.Slots.Skip(1), s => Assert.Equal((StreamKind.Sub, 1, 1), (s.Kind, s.RowSpan, s.ColumnSpan)));
    }

    [Fact]
    public void Featured_uses_selected_camera_and_keeps_others_in_order()
    {
        var cams = Cameras(4);
        var byOrder = cams.OrderBy(c => c.Order).ToList();
        var plan = ViewPlanner.Plan(cams, LayoutMode.Featured, GridMode.Auto, byOrder[2].Id);
        Assert.Equal(byOrder[2].Id, plan.Slots[0].CameraId);
        Assert.Equal(new[] { byOrder[0].Id, byOrder[1].Id, byOrder[3].Id }, plan.Slots.Skip(1).Select(s => s.CameraId).ToArray());
    }

    [Fact]
    public void Featured_unknown_id_falls_back_to_first()
    {
        var cams = Cameras(3);
        Assert.Equal(cams.OrderBy(x => x.Order).First().Id,
            ViewPlanner.Plan(cams, LayoutMode.Featured, GridMode.Auto, Guid.NewGuid()).Slots[0].CameraId);
    }

    [Fact]
    public void Featured_single_camera_fills_window_with_main()
    {
        var cam = Cameras(1);
        Assert.Equal(new TileSlot(cam[0].Id, StreamKind.Main, 0, 0, 1, 1),
            Assert.Single(ViewPlanner.Plan(cam, LayoutMode.Featured, GridMode.Auto, null).Slots));
    }
}
