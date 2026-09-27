using Centinela.Core;

namespace Centinela.Core.Tests;

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

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(5, 3, 2)]
    [InlineData(7, 4, 3)]
    [InlineData(8, 4, 3)]
    [InlineData(9, 5, 4)]
    [InlineData(10, 5, 4)]
    public void Featured_size_and_span(int count, int size, int span)
    {
        var layout = GridLayout.ComputeFeatured(count);
        Assert.Equal((size, span), (layout.Size, layout.FeaturedSpan));
        Assert.Equal(Math.Max(0, count - 1), layout.Slots.Count);
    }

    [Fact]
    public void Featured_slots_for_seven_go_right_column_then_bottom_row() =>
        Assert.Equal(new[] { (0, 3), (1, 3), (2, 3), (3, 0), (3, 1), (3, 2) },
            GridLayout.ComputeFeatured(7).Slots.ToArray());

    [Fact]
    public void Featured_slots_for_eight_fill_the_corner() =>
        Assert.Equal((3, 3), GridLayout.ComputeFeatured(8).Slots[^1]);

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(7)] [InlineData(8)] [InlineData(12)]
    public void Mirrored_featured_puts_thumbnails_on_the_left_column_then_bottom_row(int count)
    {
        var right = GridLayout.ComputeFeatured(count);
        var left = GridLayout.ComputeFeatured(count, mirrored: true);
        Assert.Equal(right.Size, left.Size);
        Assert.Equal(right.FeaturedSpan, left.FeaturedSpan);
        Assert.Equal(0, right.FeaturedColumn);
        Assert.Equal(1, left.FeaturedColumn);
        var k = left.FeaturedSpan;
        var expected = Enumerable.Range(0, k).Select(r => (r, 0))
            .Concat(Enumerable.Range(0, k + 1).Select(c => (k, c))).Take(count - 1).ToArray();
        Assert.Equal(expected, left.Slots.ToArray());
    }

    [Fact]
    public void Mirrored_featured_with_one_camera_is_single_cell()
    {
        var layout = GridLayout.ComputeFeatured(1, mirrored: true);
        Assert.Equal((1, 1, 0), (layout.Size, layout.FeaturedSpan, layout.FeaturedColumn));
        Assert.Empty(layout.Slots);
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(2, 1, 2, 1)]
    [InlineData(3, 3, 4, 2)]
    [InlineData(7, 4, 6, 3)]
    [InlineData(8, 4, 6, 3)]
    [InlineData(9, 4, 4, 2)]
    [InlineData(12, 5, 6, 3)]
    public void Dual_grid_sizes(int count, int rows, int columns, int bigSpan)
    {
        var layout = GridLayout.ComputeDual(count);
        Assert.Equal((rows, columns, bigSpan), (layout.Rows, layout.Columns, layout.BigSpan));
        Assert.Equal(Math.Min(count, 2), layout.Bigs.Count);
        Assert.Equal(Math.Max(0, count - 2), layout.Slots.Count);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
    [InlineData(15)] [InlineData(16)]
    public void Dual_cells_never_overlap_and_stay_inside_the_grid(int count)
    {
        var layout = GridLayout.ComputeDual(count);
        var used = new HashSet<(int, int)>();
        foreach (var (row, column) in layout.Bigs)
            for (var r = row; r < row + layout.BigSpan; r++)
                for (var c = column; c < column + layout.BigSpan; c++)
                {
                    Assert.InRange(r, 0, layout.Rows - 1);
                    Assert.InRange(c, 0, layout.Columns - 1);
                    Assert.True(used.Add((r, c)));
                }
        foreach (var cell in layout.Slots)
        {
            Assert.InRange(cell.Row, 0, layout.Rows - 1);
            Assert.InRange(cell.Column, 0, layout.Columns - 1);
            Assert.True(used.Add(cell));
        }
    }

    [Fact]
    public void Dual_seven_cameras_two_big_on_top_five_thumbnails_in_one_row()
    {
        var layout = GridLayout.ComputeDual(7);
        Assert.Equal([(0, 0), (0, 3)], layout.Bigs.ToArray());
        Assert.Equal([(3, 0), (3, 1), (3, 2), (3, 3), (3, 4)], layout.Slots.ToArray());
    }
}
