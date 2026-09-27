using Centinela.Core;

namespace Centinela.Core.Tests;

public class DualSelectionTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact]
    public void Click_replaces_the_oldest_big_and_alternates()
    {
        var s = new DualState([A, B], 0);
        s = DualSelection.Click(s, C);
        Assert.Equal([C, B], s.Ids.ToArray());
        Assert.Equal(1, s.NextReplace);
        s = DualSelection.Click(s, D);
        Assert.Equal([C, D], s.Ids.ToArray());
        Assert.Equal(0, s.NextReplace);
    }

    [Fact]
    public void Click_on_a_big_camera_changes_nothing()
    {
        var s = new DualState([A, B], 1);
        Assert.Same(s, DualSelection.Click(s, A));
    }

    [Fact]
    public void Click_with_fewer_than_two_bigs_appends()
    {
        Assert.Equal([A, C], DualSelection.Click(new DualState([A], 0), C).Ids.ToArray());
    }

    [Fact]
    public void Drop_replaces_the_target_and_next_click_replaces_the_other()
    {
        var s = DualSelection.Drop(new DualState([A, B], 1), C, 1);
        Assert.Equal([A, C], s.Ids.ToArray());
        Assert.Equal(0, s.NextReplace);
    }

    [Fact]
    public void Drop_of_the_other_big_swaps_them()
    {
        var s = DualSelection.Drop(new DualState([A, B], 0), B, 0);
        Assert.Equal([B, A], s.Ids.ToArray());
        Assert.Equal(1, s.NextReplace);
    }

    [Fact]
    public void Drop_on_its_own_place_or_out_of_range_changes_nothing()
    {
        var s = new DualState([A, B], 0);
        Assert.Same(s, DualSelection.Drop(s, A, 0));
        Assert.Same(s, DualSelection.Drop(s, C, 2));
    }
}
