using Centinela.Platform.Unix;

namespace Centinela.Platform.Tests;

// AF_UNIX works on Windows 10+ too, so these run here. Socket paths are limited to ~104 bytes: keep them short.
public sealed class UnixSingleInstanceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
    string SocketPath => Path.Combine(_dir, "c.sock");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void First_instance_claims()
    {
        using var first = new UnixSingleInstance(SocketPath);
        Assert.True(first.TryClaim());
        Assert.True(File.Exists(SocketPath));
    }

    [Fact]
    public void Second_claim_fails_and_wakes_the_first()
    {
        using var first = new UnixSingleInstance(SocketPath);
        Assert.True(first.TryClaim());
        using var woken = new ManualResetEventSlim();
        first.ListenForActivation(woken.Set);

        using (var second = new UnixSingleInstance(SocketPath))
            Assert.False(second.TryClaim());

        Assert.True(woken.Wait(TimeSpan.FromSeconds(5)));
        // The losing instance must not remove the winner's socket.
        Assert.True(File.Exists(SocketPath));
    }

    [Fact]
    public void Each_later_launch_wakes_the_first_again()
    {
        using var first = new UnixSingleInstance(SocketPath);
        Assert.True(first.TryClaim());
        var count = 0;
        using var twice = new CountdownEvent(2);
        first.ListenForActivation(() => { Interlocked.Increment(ref count); twice.Signal(); });

        for (var i = 0; i < 2; i++)
        {
            using var later = new UnixSingleInstance(SocketPath);
            Assert.False(later.TryClaim());
        }

        Assert.True(twice.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, count);
    }

    [Fact]
    public void Abandoned_socket_file_does_not_block_the_claim()
    {
        Directory.CreateDirectory(_dir);
        // A crashed instance leaves its file behind with nobody listening. .NET unlinks a bound socket's file when
        // it is disposed, so a plain file stands in for it: connecting to it fails just the same.
        File.WriteAllText(SocketPath, "");

        using var instance = new UnixSingleInstance(SocketPath);
        Assert.True(instance.TryClaim());
        using var woken = new ManualResetEventSlim();
        instance.ListenForActivation(woken.Set);
        using (var later = new UnixSingleInstance(SocketPath))
            Assert.False(later.TryClaim());
        Assert.True(woken.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Claim_creates_the_missing_folder()
    {
        var nested = Path.Combine(_dir, "d", "c.sock");
        using var instance = new UnixSingleInstance(nested);
        Assert.True(instance.TryClaim());
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Repeated_claim_keeps_the_first_result()
    {
        using var first = new UnixSingleInstance(SocketPath);
        Assert.True(first.TryClaim());
        Assert.True(first.TryClaim());

        using var second = new UnixSingleInstance(SocketPath);
        Assert.False(second.TryClaim());
        Assert.False(second.TryClaim());
    }

    [Fact]
    public void Listening_on_a_later_instance_throws()
    {
        using var first = new UnixSingleInstance(SocketPath);
        Assert.True(first.TryClaim());
        using var second = new UnixSingleInstance(SocketPath);
        Assert.False(second.TryClaim());
        Assert.Throws<InvalidOperationException>(() => second.ListenForActivation(() => { }));
    }

    [Fact]
    public void Dispose_removes_the_socket_file()
    {
        var instance = new UnixSingleInstance(SocketPath);
        Assert.True(instance.TryClaim());
        instance.ListenForActivation(() => { });
        instance.Dispose();
        Assert.False(File.Exists(SocketPath));

        // The next launch claims cleanly.
        using var next = new UnixSingleInstance(SocketPath);
        Assert.True(next.TryClaim());
    }

    [Fact]
    public void PathFor_uses_an_absolute_runtime_dir()
    {
        Assert.Equal(Path.Combine("/run/user/1000", "centinela.sock"),
            UnixSingleInstance.PathFor("/run/user/1000", "/home/u/.local/share/Centinela", ""));
        Assert.Equal(Path.Combine("/run/user/1000", "centinela.test-ab.sock"),
            UnixSingleInstance.PathFor("/run/user/1000", "/home/u/.local/share/Centinela", ".test-ab"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("run/user/1000")]
    public void PathFor_falls_back_to_the_data_dir(string? runtimeDir)
    {
        Assert.Equal(Path.Combine("/home/u/data", "centinela.x.sock"),
            UnixSingleInstance.PathFor(runtimeDir, "/home/u/data", ".x"));
    }
}
