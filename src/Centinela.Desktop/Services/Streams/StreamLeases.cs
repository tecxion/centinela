using Centinela.Core;
using Centinela.Media;

namespace Centinela.Desktop.Services.Streams;

/// <summary>One shared substream connection per camera, whoever is watching it.</summary>
sealed class StreamLeases : IDisposable
{
    readonly SharedStreams _streams = new();
    bool _disposed;

    /// <summary>
    /// Raised once per new shared session, before it starts, inside the shared streams' creation callback: a
    /// handler may subscribe to the session's events but must post any other work (no reentry into the leases).
    /// </summary>
    public event Action<StreamSession, Camera>? SessionCreated;

    /// <summary>The camera's substream URL; null when it has none usable.</summary>
    public static string? SubUrlOf(Camera camera)
    {
        try
        {
            return StreamUrlBuilder.Build(camera, StreamKind.Sub);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// True while the camera still uses the shared session of <paramref name="url"/> and <paramref name="useUdp"/>;
    /// false once it was edited to another substream or transport (an old session must not report under the new settings).
    /// </summary>
    public static bool UsesSession(Camera camera, string url, bool useUdp) =>
        camera.UseUdp == useUdp && SubUrlOf(camera) == url;

    /// <summary>A lease on the camera's shared substream session; null without a usable Sub URL or after Dispose.</summary>
    public SharedStreamLease? AcquireSub(Camera camera, int width, int height)
    {
        if (_disposed || SubUrlOf(camera) is not { } url) return null;
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
