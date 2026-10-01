using Centinela.Core;
using Silk.NET.OpenAL;

namespace Centinela.Desktop.Services.Audio;

/// <summary>
/// Plays the notice sounds (<see cref="NoticeTones"/>) on the default output with its own OpenAL device and
/// context, opened on the first use. Never blocks and never fails: without OpenAL (or after the device fails) it
/// stays silent. Every operation runs under <see cref="OpenAlAudioOutput.Gate"/> and makes its context current first.
/// </summary>
sealed unsafe class NoticeSoundPlayer : IDisposable
{
    // The sounds as PCM16, synthesized once outside the gate; kinds without a sound (ConnectionRecovered) are absent.
    static readonly Lazy<IReadOnlyDictionary<NoticeKind, short[]>> Tones = new(() => new Dictionary<NoticeKind, short[]>
    {
        [NoticeKind.ConnectionLost] = ToPcm(NoticeTones.ConnectionLost()),
        [NoticeKind.Motion] = ToPcm(NoticeTones.Motion()),
    });

    ALContext? _alc;
    AL? _al;
    Device* _device;
    Context* _context;
    // One source per sound, each with its buffer attached once: playing again restarts it.
    readonly Dictionary<NoticeKind, uint> _sources = [];
    readonly List<uint> _buffers = [];
    bool _opened;
    bool _unavailable;
    bool _disposed;

    /// <summary>Starts the kind's sound; kinds without one (ConnectionRecovered) stay silent.</summary>
    public void Play(NoticeKind kind)
    {
        var tones = Tones.Value; // synthesized before taking the shared lock
        if (!tones.ContainsKey(kind)) return;
        lock (OpenAlAudioOutput.Gate)
        {
            if (_disposed || _unavailable) return;
            try
            {
                if (!_opened) Open(tones);
                _alc!.MakeContextCurrent(_context);
                if (_sources.TryGetValue(kind, out var source)) _al!.SourcePlay(source);
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
    void Open(IReadOnlyDictionary<NoticeKind, short[]> tones)
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
        foreach (var (kind, pcm) in tones)
        {
            var buffer = al.GenBuffer();
            _buffers.Add(buffer);
            fixed (short* data = pcm)
                al.BufferData(buffer, BufferFormat.Mono16, data, pcm.Length * sizeof(short), NoticeTones.SampleRate);
            var source = al.GenSource();
            _sources[kind] = source;
            al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        }
        if (al.GetError() != AudioError.NoError) throw new InvalidOperationException();
    }

    static short[] ToPcm(float[] samples)
    {
        var pcm = new short[samples.Length];
        SampleConversion.ToPcm16(samples, pcm);
        return pcm;
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
