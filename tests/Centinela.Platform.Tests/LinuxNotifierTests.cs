using Centinela.Platform.Linux;

namespace Centinela.Platform.Tests;

public sealed class LinuxNotifierTests
{
    [Fact]
    public void Arguments_are_exact()
    {
        var request = NotificationRequest.Create("Cámara desconectada", "Patio: sin señal");

        Assert.Equal("Centinela", request.AppName);
        Assert.Equal(0u, request.ReplacesId);
        Assert.Equal("centinela", request.AppIcon);
        Assert.Equal("Cámara desconectada", request.Summary);
        Assert.Equal("Patio: sin señal", request.Body);
        Assert.Equal(-1, request.ExpireTimeout);
    }

    [Fact]
    public void Actions_are_the_default_action_labelled_Abrir()
    {
        var request = NotificationRequest.Create("t", "x");

        Assert.Equal(["default", "Abrir"], request.Actions);
        Assert.Equal("default", NotificationRequest.DefaultAction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_title_becomes_Centinela(string? title)
        => Assert.Equal("Centinela", NotificationRequest.Create(title, "x").Summary);

    [Fact]
    public void Null_text_becomes_empty()
        => Assert.Equal("", NotificationRequest.Create("t", null).Body);

    [Fact]
    public void Text_of_1000_characters_is_kept_whole()
    {
        var text = new string('a', 1000);
        Assert.Equal(text, NotificationRequest.Create("t", text).Body);
    }

    [Fact]
    public void Longer_text_is_cut_to_1000_characters()
    {
        var text = new string('a', 999) + "bcd";
        Assert.Equal(new string('a', 999) + "b", NotificationRequest.Create("t", text).Body);
    }

    [Fact]
    public void Cut_never_splits_a_surrogate_pair()
    {
        var text = new string('a', 999) + "😀" + "zz"; // the emoji takes characters 1000 and 1001
        Assert.Equal(new string('a', 999), NotificationRequest.Create("t", text).Body);
    }
}
