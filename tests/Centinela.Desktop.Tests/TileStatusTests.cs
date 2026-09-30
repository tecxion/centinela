using Centinela.Core;
using Centinela.Desktop.ViewModels;
using Centinela.Media;

namespace Centinela.Desktop.Tests;

public class TileStatusTests
{
    static readonly Camera Cam = new() { Name = "Patio", Password = "x", Brand = Brand.Tapo };

    [Theory]
    [InlineData(SessionState.Connecting, "Conectando…")]
    [InlineData(SessionState.Reconnecting, "Reconectando…")]
    [InlineData(SessionState.Stopped, "Detenida")]
    [InlineData(SessionState.Playing, "")]
    [InlineData(SessionState.Idle, "")]
    public void States_without_an_error(SessionState state, string expected) =>
        Assert.Equal(expected, TileStatus.Text(state, null, Cam));

    [Fact]
    public void An_error_stays_on_screen_while_retrying()
    {
        var shortText = ErrorTranslator.Translate(StreamErrorKind.Unreachable, Brand.Tapo, "Patio").Short;
        Assert.Equal($"{shortText} · reintentando", TileStatus.Text(SessionState.Reconnecting, StreamErrorKind.Unreachable, Cam));
        Assert.Equal($"{shortText} · reintentando", TileStatus.Text(SessionState.Connecting, StreamErrorKind.Unreachable, Cam));
    }

    [Fact]
    public void Auth_failure_is_shown_without_retrying()
    {
        var shortText = ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Tapo, "Patio").Short;
        Assert.Equal(shortText, TileStatus.Text(SessionState.AuthFailed, StreamErrorKind.AuthFailed, Cam));
        Assert.Equal(shortText, TileStatus.Text(SessionState.AuthFailed, null, Cam));
    }

    [Fact]
    public void Playing_clears_any_error() =>
        Assert.Equal("", TileStatus.Text(SessionState.Playing, StreamErrorKind.Unreachable, Cam));

    [Theory]
    [InlineData(SessionState.Reconnecting, true)]
    [InlineData(SessionState.AuthFailed, true)]
    [InlineData(SessionState.Idle, false)]
    [InlineData(SessionState.Connecting, false)]
    [InlineData(SessionState.Playing, false)]
    [InlineData(SessionState.Stopped, false)]
    public void Failing_states_retire_a_placeholder(SessionState state, bool expected) =>
        Assert.Equal(expected, TileStatus.IsFailing(state));
}
