using Centinela.Core;
using Centinela.Media;

namespace Centinela.Desktop.Services.Streams;

/// <summary>One shared substream connection per camera, whoever is watching it.</summary>
sealed class StreamLeases : IDisposable
{
    readonly SharedStreams _streams = new();
    bool _disposed;

    /// <summary>Raised once per new shared session, before it starts (error reporting hooks in, phase 2d).</summary>
    public event Action<StreamSession, Camera>? SessionCreated;

    /// <summary>A lease on the camera's shared substream session; null without a usable Sub URL or after Dispose.</summary>
    public SharedStreamLease? AcquireSub(Camera camera, int width, int height)
    {
        if (_disposed) return null;
        string url;
        try
        {
            url = StreamUrlBuilder.Build(camera, StreamKind.Sub);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        try
        {
            return _streams.Acquire(camera.Id, url, camera.UseUdp, width, height, session =>
            {
                session.Smoothing = camera.Smoothing;
                SessionCreated?.Invoke(session, camera);
            });
        }
        catch (ObjectDisposedException)
        {
            return null; // closing
        }
    }

    public IReadOnlyList<StreamSession> SessionsFor(Guid cameraId) => _streams.SessionsFor(cameraId);

    public void Dispose()
    {
        _disposed = true;
        _streams.Dispose();
    }
}
