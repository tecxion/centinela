using Silk.NET.OpenAL;

namespace Centinela.Desktop.Services.Audio;

/// <summary>
/// Plays the notice sounds (<see cref="NoticeTones"/>) on the default output with its own OpenAL device and
/// context, opened on the first use. Never blocks and never fails: without OpenAL (or after the device fails) it
/// stays silent. Every operation runs under <see cref="OpenAlAudioOutput.Gate"/> and makes its context current first.
/// </summary>
sealed unsafe class NoticeSoundPlayer : IDisposable
{
    // One source per sound, each with its buffer attached once: playing again restarts it.
    static readonly NoticeKind[] Kinds = [NoticeKind.ConnectionLost, NoticeKind.Motion];

    ALContext? _alc;
    AL? _al;
    Device* _device;
    Context* _context;
    readonly Dictionary<NoticeKind, uint> _sources = [];
    readonly List<uint> _buffers = [];
    bool _opened;
    bool _unavailable;
    bool _disposed;

    public void Play(NoticeKind kind)
    {
        lock (OpenAlAudioOutput.Gate)
        {
            if (_disposed || _unavailable) return;
            try
            {
                if (!_opened) Open();
                _alc!.MakeContextCurrent(_context);
                _al!.SourcePlay(_sources[kind]);
            }
            catch (Exception)
            {
                // No OpenAL or no device: notices just stay silent.
                Release();
                _unavailable = true;
            }
        }
    }

    /// <summary>Under the gate. Throws when there is no usable output (the caller releases what was made).</summary>
    void Open()
    {
        _opened = true;
        var (alc, al) = OpenAlAudioOutput.LoadApi();
        _alc = alc;
        _al = al;
        _device = alc.OpenDevice(null); // null: the system's default output
        if (_device is null) throw new InvalidOperationException();
        _context = alc.CreateContext(_device, null);
        if (_context is null || !alc.MakeContextCurrent(_context)) throw new InvalidOperationException();
        al.GetError(); // clear any error left by another output
        foreach (var kind in Kinds)
        {
            var buffer = al.GenBuffer();
            _buffers.Add(buffer);
            Load(buffer, kind == NoticeKind.ConnectionLost ? NoticeTones.ConnectionLost() : NoticeTones.Motion());
            var source = al.GenSource();
            _sources[kind] = source;
            al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        }
        if (al.GetError() != AudioError.NoError) throw new InvalidOperationException();
    }

    void Load(uint buffer, float[] samples)
    {
        var pcm = new short[samples.Length];
        SampleConversion.ToPcm16(samples, pcm);
        fixed (short* data = pcm)
            _al!.BufferData(buffer, BufferFormat.Mono16, data, pcm.Length * sizeof(short), NoticeTones.SampleRate);
    }

    /// <summary>Under the gate: sources, buffers, context and device; never throws.</summary>
    void Release()
    {
        if (_alc is null || _al is null) return;
        var alc = _alc;
        var al = _al;
        if (_context is not null)
        {
            Quietly(() => alc.MakeContextCurrent(_context));
            foreach (var source in _sources.Values)
            {
                Quietly(() => al.SourceStop(source));
                Quietly(() => al.DeleteSource(source));
            }
            foreach (var buffer in _buffers) Quietly(() => al.DeleteBuffer(buffer));
            Quietly(() => alc.MakeContextCurrent(IntPtr.Zero));
            Quietly(() => alc.DestroyContext(_context));
        }
        if (_device is not null) Quietly(() => alc.CloseDevice(_device));
        _sources.Clear();
        _buffers.Clear();
        _context = null;
        _device = null;
    }

    public void Dispose()
    {
        lock (OpenAlAudioOutput.Gate)
        {
            if (_disposed) return;
            _disposed = true;
            Release();
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
