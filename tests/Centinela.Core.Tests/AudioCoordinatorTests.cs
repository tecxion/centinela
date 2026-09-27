using Centinela.Core;

namespace Centinela.Core.Tests;

public class AudioCoordinatorTests
{
    sealed class Source;

    [Fact]
    public void Only_one_source_is_active_and_the_previous_is_returned_to_silence()
    {
        var audio = new AudioCoordinator<Source>();
        Source a = new(), b = new();
        Assert.Null(audio.Activate(a));
        Assert.Same(a, audio.Activate(b));
        Assert.Same(b, audio.Active);
        Assert.Null(audio.Activate(b));
    }

    [Fact]
    public void Deactivate_only_affects_the_active_source()
    {
        var audio = new AudioCoordinator<Source>();
        Source a = new(), b = new();
        audio.Activate(a);
        Assert.False(audio.Deactivate(b));
        Assert.Same(a, audio.Active);
        Assert.True(audio.Deactivate(a));
        Assert.Null(audio.Active);
    }
}
