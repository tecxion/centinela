using System.Diagnostics;
using CamaraWin.Media.Tests.Rtsp;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class RecordingTests(RtspTestServer server) : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-rec-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static string Probe(string args)
    {
        var ffprobe = TestUtil.FindOnPath("ffprobe.exe") ?? throw new InvalidOperationException("ffprobe not on PATH");
        using var p = Process.Start(new ProcessStartInfo(ffprobe, args)
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
        })!;
        var text = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return text;
    }

    StreamSession OpenHeadless()
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(server.Url("open"), decode: false);
    }

    [SkippableFact]
    public async Task Records_a_valid_mkv_that_starts_on_a_keyframe()
    {
        var path = Path.Combine(_dir, "cam", "clip.mkv");
        using var session = OpenHeadless();
        session.StartRecording(() => path);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)), session.LastError);
        Thread.Sleep(3000);

        await session.StopRecordingAsync();
        session.Stop();

        Assert.False(session.IsRecording);
        Assert.Equal("h264,640,360", Probe($"-v error -select_streams v:0 -show_entries stream=codec_name,width,height -of csv=p=0 \"{path}\""));
        var duration = double.Parse(Probe($"-v error -show_entries format=duration -of csv=p=0 \"{path}\""),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(duration, 1.5, 6);
        Assert.Equal("1", Probe($"-v error -select_streams v:0 -read_intervals %+#1 -show_entries frame=key_frame -of csv=p=0 \"{path}\""));
    }

    [SkippableFact]
    public void Unwritable_path_raises_RecordingFailed()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "file where a folder should be");

        using var session = OpenHeadless();
        string? failure = null;
        session.RecordingFailed += message => failure = message;
        session.StartRecording(() => Path.Combine(blocker, "sub", "clip.mkv"));
        session.Start();

        Assert.True(TestUtil.WaitFor(() => failure is not null, TimeSpan.FromSeconds(10)));
        Assert.False(session.IsRecording);
    }

    static double Duration(string path) =>
        double.Parse(Probe($"-v error -show_entries format=duration -of csv=p=0 \"{path}\""),
            System.Globalization.CultureInfo.InvariantCulture);

    [SkippableFact]
    public void Stop_without_StopRecording_still_finalizes_mkv()
    {
        var path = Path.Combine(_dir, "cam", "clip.mkv");
        using var session = OpenHeadless();
        session.StartRecording(() => path);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)), session.LastError);
        Thread.Sleep(3000);

        session.Stop(); // no StopRecordingAsync: Stop alone must leave a finished file

        Assert.InRange(Duration(path), 1.5, 6);
    }

    [SkippableFact]
    public void IsRecording_false_after_session_stopped()
    {
        var path = Path.Combine(_dir, "cam", "clip.mkv");
        using var session = OpenHeadless();
        session.StartRecording(() => path);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)), session.LastError);

        session.Stop();

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.False(session.IsRecording);
    }

    [SkippableFact]
    public async Task Existing_file_is_not_overwritten()
    {
        var path = Path.Combine(_dir, "cam", "clip.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] dummy = [1, 2, 3, 4, 5];
        File.WriteAllBytes(path, dummy);

        using var session = OpenHeadless();
        session.StartRecording(() => path);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)), session.LastError);
        Thread.Sleep(2000);
        await session.StopRecordingAsync();
        session.Stop();

        Assert.Equal(dummy, File.ReadAllBytes(path));
        var renamed = Path.Combine(_dir, "cam", "clip_1.mkv");
        Assert.True(File.Exists(renamed), "recording should go to clip_1.mkv");
        Assert.Equal("h264,640,360", Probe($"-v error -select_streams v:0 -show_entries stream=codec_name,width,height -of csv=p=0 \"{renamed}\""));
    }
}
