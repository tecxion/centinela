using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public sealed unsafe partial class StreamSession
{
    static readonly TimeSpan RecorderDrainTimeout = TimeSpan.FromSeconds(3);

    readonly object _recordLock = new();
    Func<string>? _recordPathFactory; // non-null while recording is requested
    Recorder? _recorder;              // the file currently receiving packets
    // Every recorder whose file is not finished yet (active or draining), mapped to the task that
    // completes after its file is closed and any failure has been reported.
    readonly Dictionary<Recorder, Task> _unfinished = [];

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

    /// <summary>
    /// Stops recording; completes once every file this session was still writing has been finalized.
    /// Faults with a <see cref="RecordingException"/> carrying the recorder errors if any of those files failed.
    /// </summary>
    public Task StopRecordingAsync()
    {
        Recorder? recorder;
        IReadOnlyList<(Task, Func<string?>)> draining;
        lock (_recordLock)
        {
            _recordPathFactory = null;
            recorder = _recorder;
            _recorder = null;
            // Snapshot before Complete() so a file that finishes at once still has its error reported.
            draining = PendingRecorders();
        }
        recorder?.Complete();

        return RecorderFinalization.WhenAllFinalizedAsync(draining, PendingRecorders);
    }

    /// <summary>Called by Stop() after the session thread ended: bounded wait so files get their trailer.</summary>
    void WaitForRecorders()
    {
        Task[] pending;
        lock (_recordLock) pending = [.. _unfinished.Values];
        if (pending.Length > 0) Task.WaitAll(pending, RecorderDrainTimeout);
    }

    IReadOnlyList<(Task, Func<string?>)> PendingRecorders()
    {
        lock (_recordLock)
            return [.. _unfinished.Where(entry => entry.Key != _recorder)
                .Select(entry => (entry.Value, (Func<string?>)(() => entry.Key.Error)))];
    }

    partial void OnVideoPacketCore(AVStream* stream, AVPacket* pkt)
    {
        Func<string>? factory;
        Recorder? recorder;
        lock (_recordLock)
        {
            factory = _recordPathFactory;
            recorder = _recorder;
            if (factory is null) return;
            if (recorder is null && (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0) return;
        }

        if (recorder is null)
        {
            // File creation (disk I/O) and the caller's path factory run outside the lock.
            try
            {
                recorder = new Recorder(factory(), stream->codecpar, stream->time_base);
            }
            catch (Exception ex)
            {
                lock (_recordLock)
                    if (_recordPathFactory == factory) _recordPathFactory = null;
                RaiseRecordingFailed(ex.Message);
                return;
            }

            bool accepted;
            lock (_recordLock)
            {
                Track(recorder);
                accepted = _recordPathFactory == factory && _recorder is null;
                if (accepted) _recorder = recorder;
            }
            if (!accepted)
            {
                recorder.Complete(); // recording was stopped or restarted while the file was being created
                return;
            }
        }

        string? failure = null;
        var stop = false;
        lock (_recordLock)
        {
            if (_recorder != recorder) return;
            if (!recorder.Enqueue(pkt))
            {
                // Recorder errors are reported by its completion (see Track); only report the rest here.
                if (recorder.Error is null) failure = "Grabación detenida.";
                _recorder = null;
                _recordPathFactory = null;
                stop = true;
            }
        }
        if (stop) recorder.Complete();
        if (failure is not null) RaiseRecordingFailed(failure);
    }

    /// <summary>Must be called under _recordLock.</summary>
    void Track(Recorder recorder)
    {
        _unfinished[recorder] = recorder.Completion.ContinueWith(_ =>
        {
            lock (_recordLock) _unfinished.Remove(recorder);
            if (recorder.Error is { } error) RaiseRecordingFailed(error);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    void RaiseRecordingFailed(string message)
    {
        try
        {
            RecordingFailed?.Invoke(message);
        }
        catch (Exception)
        {
            // A faulty subscriber must not break the stream or the recorder bookkeeping.
        }
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

    partial void OnSessionEndingCore()
    {
        lock (_recordLock) _recordPathFactory = null;
        OnConnectionClosedCore();
    }
}
