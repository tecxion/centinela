using System.Collections.Concurrent;
using Centinela.Media.Tests.Rtsp;

namespace Centinela.Media.Tests;

[Collection("rtsp")]
public sealed class AudioTests(RtspTestServer server)
{
    sealed class CollectingSink : IAudioSink
    {
        public readonly ConcurrentQueue<int> Chunks = new();
        public long Samples;
        public void Write(ReadOnlySpan<float> samples)
        {
            Chunks.Enqueue(samples.Length);
            Interlocked.Add(ref Samples, samples.Length);
        }
    }

    StreamSession Open(string path)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(server.Url(path));
    }

    [SkippableFact]
    public void With_a_sink_audio_arrives_as_interleaved_stereo_at_48k()
    {
        using var session = Open("av");
        var sink = new CollectingSink();
        session.SetAudioSink(sink);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => Interlocked.Read(ref sink.Samples) >= AudioFormat.SampleRate * AudioFormat.Channels / 2,
            TimeSpan.FromSeconds(10)), "less than 0.5 s of audio in 10 s");
        Assert.All(sink.Chunks, n => Assert.Equal(0, n % AudioFormat.Channels));
    }

    [SkippableFact]
    public void Without_a_sink_audio_is_not_decoded()
    {
        using var session = Open("av");
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        Thread.Sleep(1500);
        Assert.Equal(0, session.AudioFramesDecoded);
    }

    [SkippableFact]
    public void Sink_can_be_attached_and_removed_without_reconnecting()
    {
        using var session = Open("av");
        var states = new ConcurrentQueue<SessionState>();
        session.StateChanged += states.Enqueue;
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        var sink = new CollectingSink();
        session.SetAudioSink(sink);
        Assert.True(TestUtil.WaitFor(() => Interlocked.Read(ref sink.Samples) > 0, TimeSpan.FromSeconds(5)));
        session.SetAudioSink(null);
        Thread.Sleep(500);
        var after = Interlocked.Read(ref sink.Samples);
        Thread.Sleep(1000);
        Assert.Equal(after, Interlocked.Read(ref sink.Samples));
        Assert.DoesNotContain(SessionState.Reconnecting, states);
    }

    [SkippableFact]
    public void Stream_without_audio_plays_normally_with_a_sink()
    {
        using var session = Open("open");
        session.SetAudioSink(new CollectingSink());
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
    }
}
