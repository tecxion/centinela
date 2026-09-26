using System.Buffers.Binary;
using CamaraWin.Media.Tests.Rtsp;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class SnapshotTests(RtspTestServer server) : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-snap-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [SkippableFact]
    public async Task Saves_png_at_native_resolution_even_when_scaled()
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        using var session = new StreamSession(server.Url("open"));
        session.SetTargetSize(160, 90);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 0, TimeSpan.FromSeconds(10)), session.LastError);

        var path = Path.Combine(_dir, "snap.png");
        Assert.True(await session.SaveSnapshotAsync(path));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(640, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(360, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    [Fact]
    public async Task Returns_false_before_first_frame()
    {
        FFmpegLoader.Initialize();
        using var session = new StreamSession("rtsp://127.0.0.1:1/none");
        Assert.False(await session.SaveSnapshotAsync(Path.Combine(_dir, "none.png")));
    }
}
