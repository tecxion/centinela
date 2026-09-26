using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public sealed unsafe partial class StreamSession
{
    readonly object _recordLock = new();
    Func<string>? _recordPathFactory; // non-null while recording is requested
    Recorder? _recorder;

    public bool IsRecording
    {
        get { lock (_recordLock) return _recordPathFactory is not null; }
    }

    public event Action<string>? RecordingFailed;

    /// <summary>Records from the next keyframe. After a reconnect a new file is started.</summary>
    public void StartRecording(Func<string> pathFactory)
    {
        lock (_recordLock) _recordPathFactory = pathFactory;
    }

    public Task StopRecordingAsync()
    {
        Recorder? recorder;
        lock (_recordLock)
        {
            _recordPathFactory = null;
            recorder = _recorder;
            _recorder = null;
        }
        recorder?.Complete();
        return recorder?.Completion ?? Task.CompletedTask;
    }

    partial void OnVideoPacketCore(AVStream* stream, AVPacket* pkt)
    {
        string? failure = null;
        lock (_recordLock)
        {
            if (_recordPathFactory is null) return;
            if (_recorder is null)
            {
                if ((pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0) return;
                try
                {
                    _recorder = new Recorder(_recordPathFactory(), stream->codecpar, stream->time_base);
                }
                catch (Exception ex)
                {
                    _recordPathFactory = null;
                    failure = ex.Message;
                }
            }
            if (_recorder is not null && !_recorder.Enqueue(pkt))
            {
                failure = _recorder.Error ?? "Grabación detenida.";
                _recorder.Complete();
                _recorder = null;
                _recordPathFactory = null;
            }
        }
        if (failure is not null) RecordingFailed?.Invoke(failure);
    }

    partial void OnConnectionClosedCore()
    {
        Recorder? recorder;
        lock (_recordLock)
        {
            recorder = _recorder;
            _recorder = null;
        }
        recorder?.Complete();
    }

    partial void OnSessionEndingCore() => OnConnectionClosedCore();
}
