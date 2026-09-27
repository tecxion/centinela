using System.Runtime.InteropServices;
using Centinela.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Centinela.App;

/// <summary>WASAPI shared-mode output (50 ms). Keeps at most 150 ms queued: lagging audio is dropped, never delayed.</summary>
sealed class AudioOutput : IAudioSink, IDisposable
{
    static readonly TimeSpan MaxQueued = TimeSpan.FromMilliseconds(150);
    readonly WasapiOut _output;
    readonly BufferedWaveProvider _buffer;
    volatile bool _disposing;

    /// <summary>
    /// The device stopped with an error (unplugged, disabled, lost). Raised on the thread that created the
    /// output when it has a synchronization context, otherwise on the playback thread; never during <see cref="Dispose"/>.
    /// </summary>
    public event Action<AudioOutput, string>? Failed;

    // Per audio thread: while the sink moves from one camera to another, the old pump may still be writing.
    [ThreadStatic] static byte[]? t_bytes;

    AudioOutput()
    {
        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        _output = new WasapiOut(AudioClientShareMode.Shared, 50);
        _output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } error && !_disposing) Failed?.Invoke(this, error.Message);
        };
        try
        {
            _output.Init(_buffer);
            _output.Play();
        }
        catch
        {
            _output.Dispose();
            throw;
        }
    }

    /// <summary>Null when Windows has no usable output device.</summary>
    public static AudioOutput? TryCreate()
    {
        try { return new AudioOutput(); }
        catch (Exception) { return null; }
    }

    /// <summary>Audio thread. BufferedWaveProvider copies the samples, so the scratch array can be reused.</summary>
    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        if (_buffer.BufferedDuration > MaxQueued) _buffer.ClearBuffer();
        var bytes = MemoryMarshal.AsBytes(interleavedStereo);
        var scratch = t_bytes;
        if (scratch is null || scratch.Length < bytes.Length) t_bytes = scratch = new byte[bytes.Length];
        bytes.CopyTo(scratch);
        _buffer.AddSamples(scratch, 0, bytes.Length);
    }

    public void Clear() => _buffer.ClearBuffer();

    /// <summary>Also safe after the device failed (errors from the dead device are ignored).</summary>
    public void Dispose()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            _output.Stop();
            _output.Dispose();
        }
        catch (Exception)
        {
            // The device is already gone; nothing left to release that would not throw again.
        }
    }
}
