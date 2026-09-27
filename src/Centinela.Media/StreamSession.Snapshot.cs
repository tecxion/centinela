using FFmpeg.AutoGen;

namespace Centinela.Media;

public sealed unsafe partial class StreamSession
{
    /// <summary>Saves the last decoded frame at its native resolution. False if nothing was decoded yet.</summary>
    public Task<bool> SaveSnapshotAsync(string path) => Task.Run(() =>
    {
        AVFrame* copy;
        lock (_snapshotLock)
        {
            if (_lastFrame is null) return false;
            copy = ffmpeg.av_frame_clone(_lastFrame);
        }
        try
        {
            SnapshotWriter.SavePng(copy, path);
            return true;
        }
        finally
        {
            ffmpeg.av_frame_free(&copy);
        }
    });
}
