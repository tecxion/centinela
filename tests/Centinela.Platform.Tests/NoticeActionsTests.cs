using Centinela.Platform.MacOS;

namespace Centinela.Platform.Tests;

public class NoticeActionsTests
{
    [Fact]
    public void Take_returns_the_action_once_and_then_null()
    {
        var actions = new NoticeActions();
        Action click = () => { };
        var id = actions.Add(click);

        Assert.Same(click, actions.Take(id));
        Assert.Null(actions.Take(id));
    }

    [Fact]
    public void An_unknown_id_has_no_action()
    {
        var actions = new NoticeActions();
        actions.Add(() => { });

        Assert.Null(actions.Take("unknown"));
    }

    [Fact]
    public void Past_the_capacity_the_oldest_action_is_dropped()
    {
        var actions = new NoticeActions(capacity: 2);
        Action first = () => { }, second = () => { }, third = () => { };
        var firstId = actions.Add(first);
        var secondId = actions.Add(second);
        var thirdId = actions.Add(third);

        Assert.Null(actions.Take(firstId));
        Assert.Same(second, actions.Take(secondId));
        Assert.Same(third, actions.Take(thirdId));
    }

    [Fact]
    public void Ids_do_not_repeat()
    {
        var actions = new NoticeActions();
        var ids = Enumerable.Range(0, 200).Select(_ => actions.Add(() => { })).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
