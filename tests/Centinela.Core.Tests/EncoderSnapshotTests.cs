using Centinela.Core.Onvif;

namespace Centinela.Core.Tests;

public class EncoderSnapshotTests
{
    [Fact]
    public void Round_trips_every_configuration()
    {
        var text = EncoderSnapshot.Format([
            ("VideoEncoder000", new EncoderSettings("Main", "H264", 2560, 1440, 12, 1536, null, 48)),
            ("VideoEncoder001", new EncoderSettings("Sub", "H264", 640, 480, 15, 512, null, 60)),
        ]);
        Assert.Equal("VideoEncoder000=2560x1440@12:1536;VideoEncoder001=640x480@15:512", text);
        var parsed = EncoderSnapshot.Parse(text);
        Assert.Equal(new EncoderChange(2560, 1440, 12, 1536), parsed["VideoEncoder000"]);
        Assert.Equal(new EncoderChange(640, 480, 15, 512), parsed["VideoEncoder001"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage;=1x2@3:4;a=1x2@3;b=-1x2@3:4")]
    public void Malformed_text_gives_nothing(string? text) => Assert.Empty(EncoderSnapshot.Parse(text));
}
