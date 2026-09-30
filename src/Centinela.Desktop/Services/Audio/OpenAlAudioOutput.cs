using Centinela.Media;
using Silk.NET.OpenAL;

namespace Centinela.Desktop.Services.Audio;

/// <summary>
/// OpenAL Soft output on the default device: one source fed with 16-bit stereo buffers at 48 kHz. Keeps at most
/// 150 ms queued: lagging audio is dropped, never delayed. Writes come from the session's audio thread.
/// </summary>
sealed unsafe class OpenAlAudioOutput : IAudioSink, IDisposable
{
    const int MaxQueuedFrames = AudioFormat.SampleRate * 150 / 1000;
    // ALC_CONNECTED from ALC_EXT_disconnect; Silk.NET's GetContextInteger has no member for it.
    const GetContextInteger Connected = (GetContextInteger)0x313;
    const string DisconnectedMessage = "Se desconectó el dispositivo de audio.";

    // One lock for every output and for loading the API: the OpenAL context is current process-wide, so each
    // locked operation makes its own context current first.
    static readonly object Gate = new();
    static ALContext? s_alc;
    static AL? s_al;

    readonly ALContext _alc;
    readonly AL _al;
    readonly Device* _device;
    readonly Context* _context;
    readonly uint _source;
    readonly bool _detectsDisconnect;
    readonly Queue<(uint Buffer, int Frames)> _queued = new(); // oldest first, as OpenAL plays them
    readonly Stack<uint> _free = new();                          // played buffers, reused
    readonly List<uint> _buffers = [];                           // every buffer generated, deleted on Dispose
    int _queuedFrames;
    short[] _pcm = [];
    bool _failed;
    bool _disposed;

    /// <summary>
    /// The device failed or was disconnected. Raised once, on the audio thread that was writing; never during
    /// <see cref="Dispose"/>. Later writes are ignored.
    /// </summary>
    public event Action<OpenAlAudioOutput, string>? Failed;

    OpenAlAudioOutput(ALContext alc, AL al, Device* device, Context* context, uint source)
    {
        _alc = alc;
        _al = al;
        _device = device;
        _context = context;
        _source = source;
        _detectsDisconnect = alc.IsExtensionPresent(device, "ALC_EXT_disconnect");
    }

    /// <summary>Null when there is no usable output device (or OpenAL Soft cannot be loaded).</summary>
    public static OpenAlAudioOutput? TryCreate()
    {
        lock (Gate)
        {
            Device* device = null;
            Context* context = null;
            ALContext? alc = null;
            try
            {
                alc = s_alc ??= ALContext.GetApi(soft: true);
                var al = s_al ??= AL.GetApi(soft: true);
                device = alc.OpenDevice(null); // null: the system's default output
                if (device is null) return null;
                context = alc.CreateContext(device, null);
                if (context is null || !alc.MakeContextCurrent(context)) throw new InvalidOperationException();
                al.GetError(); // clear any error left by an earlier output
                var source = al.GenSource();
                if (al.GetError() != AudioError.NoError) throw new InvalidOperationException();
                return new OpenAlAudioOutput(alc, al, device, context, source);
            }
            catch (Exception)
            {
                Quietly(() => alc?.MakeContextCurrent(IntPtr.Zero));
                if (context is not null) Quietly(() => alc!.DestroyContext(context));
                if (device is not null) Quietly(() => alc!.CloseDevice(device));
                return null;
            }
        }
    }

    /// <summary>Audio thread. The samples are copied into an OpenAL buffer, so the caller may reuse the span.</summary>
    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        string? failure;
        lock (Gate)
        {
            if (_disposed || _failed || interleavedStereo.Length < AudioFormat.Channels) return;
            try
            {
                failure = Enqueue(interleavedStereo);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            if (failure is not null) _failed = true;
        }
        if (failure is not null) Failed?.Invoke(this, failure);
    }

    /// <summary>Drops whatever is queued (another camera starts playing).</summary>
    public void Clear()
    {
        lock (Gate)
        {
            if (_disposed || _failed) return;
            try
            {
                _alc.MakeContextCurrent(_context);
                DropQueued();
            }
            catch (Exception)
            {
                // A dead device is reported by the next Write.
            }
        }
    }

    /// <summary>Under <see cref="Gate"/>. Returns the failure message, or null when the samples were queued.</summary>
    string? Enqueue(ReadOnlySpan<float> samples)
    {
        _alc.MakeContextCurrent(_context);
        if (!IsConnected()) return DisconnectedMessage;
        Reclaim();
        if (_queuedFrames > MaxQueuedFrames) DropQueued();

        var frames = samples.Length / AudioFormat.Channels;
        var count = frames * AudioFormat.Channels;
        if (_pcm.Length < count) _pcm = new short[count];
        SampleConversion.ToPcm16(samples[..count], _pcm);

        var buffer = NextBuffer();
        fixed (short* data = _pcm)
            _al.BufferData(buffer, BufferFormat.Stereo16, data, count * sizeof(short), AudioFormat.SampleRate);
        _al.SourceQueueBuffers(_source, 1, &buffer);
        if (_al.GetError() is var error && error != AudioError.NoError)
        {
            _free.Push(buffer);
            return $"OpenAL: {error}";
        }
        _queued.Enqueue((buffer, frames));
        _queuedFrames += frames;

        // Stopped after running dry (or after DropQueued): start again with what is queued now.
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
        if (state != (int)SourceState.Playing) _al.SourcePlay(_source);
        return null;
    }

    bool IsConnected()
    {
        if (!_detectsDisconnect) return true;
        int connected;
        _alc.GetContextProperty(_device, Connected, 1, &connected);
        return connected != 0;
    }

    uint NextBuffer()
    {
        if (_free.TryPop(out var buffer)) return buffer;
        buffer = _al.GenBuffer();
        _buffers.Add(buffer);
        return buffer;
    }

    /// <summary>Takes back the buffers already played, for reuse.</summary>
    void Reclaim()
    {
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        for (; processed > 0 && _queued.Count > 0; processed--) Unqueue();
    }

    /// <summary>Stops the source (every buffer becomes processed) and takes them all back.</summary>
    void DropQueued()
    {
        _al.SourceStop(_source);
        while (_queued.Count > 0) Unqueue();
    }

    void Unqueue()
    {
        uint buffer;
        _al.SourceUnqueueBuffers(_source, 1, &buffer);
        var (_, frames) = _queued.Dequeue();
        _queuedFrames -= frames;
        _free.Push(buffer);
    }

    /// <summary>Releases source, buffers, context and device; never throws (also after the device failed).</summary>
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            Quietly(() => _alc.MakeContextCurrent(_context));
            Quietly(() => _al.SourceStop(_source));
            Quietly(() => _al.DeleteSource(_source)); // also detaches the queued buffers
            foreach (var buffer in _buffers) Quietly(() => _al.DeleteBuffer(buffer));
            Quietly(() => _alc.MakeContextCurrent(IntPtr.Zero));
            Quietly(() => _alc.DestroyContext(_context));
            Quietly(() => _alc.CloseDevice(_device));
        }
    }

    static void Quietly(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // The device is already gone; nothing left to release that would not fail again.
        }
    }
}
