using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class TrayTextTests
{
    [Fact]
    public void Short_text_is_unchanged()
    {
        Assert.Equal("Centinela", TrayText.Fit("Centinela", TrayText.TipMax));
        Assert.Equal("Patio: movimiento", TrayText.Fit("Patio: movimiento", TrayText.TitleMax));
    }

    [Fact]
    public void Long_text_is_cut_to_the_limit_with_an_ellipsis()
    {
        var fitted = TrayText.Fit(new string('a', 300), TrayText.InfoMax);
        Assert.Equal(255, fitted.Length);
        Assert.EndsWith("…", fitted);
        Assert.Equal(new string('a', 254), fitted[..254]);
    }

    [Fact]
    public void Text_exactly_at_the_limit_is_unchanged()
    {
        var text = new string('b', TrayText.TitleMax);
        Assert.Equal(text, TrayText.Fit(text, TrayText.TitleMax));
    }

    [Fact]
    public void Empty_text_becomes_a_space_like_the_1_3_balloon()
    {
        Assert.Equal(" ", TrayText.Fit("", TrayText.InfoMax));
    }

    [Fact]
    public void Limits_leave_room_for_the_terminator_of_notifyicondata()
    {
        Assert.Equal(127, TrayText.TipMax);
        Assert.Equal(63, TrayText.TitleMax);
        Assert.Equal(255, TrayText.InfoMax);
    }
}
