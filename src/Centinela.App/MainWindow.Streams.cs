using Centinela.Core;
using Centinela.Media;

namespace Centinela.App;

/// <summary>Shared substream sessions: one connection per camera substream, whoever is watching it.</summary>
public partial class MainWindow
{
    readonly SharedStreams _streams = new();

    /// <summary>
    /// A lease on the camera's shared substream session, decoding at least <paramref name="width"/>×<paramref name="height"/>
    /// (0 = until the holder sets its size). Null when the camera has no usable Sub URL or the window has closed.
    /// The session's errors and recoveries are reported to the error center once, whoever holds leases on it.
    /// </summary>
    internal SharedStreamLease? AcquireSub(Camera camera, int width, int height)
    {
        if (_closed) return null;
        string url;
        try
        {
            url = StreamUrlBuilder.Build(camera, StreamKind.Sub);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        var useUdp = camera.UseUdp;
        try
        {
            return _streams.Acquire(camera.Id, url, useUdp, width, height, session =>
            {
                session.Smoothing = camera.Smoothing;
                WireSharedErrors(session, camera.Id, url, useUdp);
            });
        }
        catch (ObjectDisposedException)
        {
            return null; // closing
        }
    }

    /// <summary>Runs once per shared session, before it starts.</summary>
    void WireSharedErrors(StreamSession session, Guid cameraId, string url, bool useUdp)
    {
        session.ErrorOccurred += error => Dispatcher.BeginInvoke(() =>
        {
            if (CurrentCameraFor(cameraId, url, useUdp) is { } camera) _errors.Report(camera, error);
        });
        session.StateChanged += state =>
        {
            if (state != SessionState.Playing) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (CurrentCameraFor(cameraId, url, useUdp) is { } camera) _errors.Playing(camera);
            });
        };
    }

    /// <summary>
    /// The current camera with that Id (tiles may hold older copies), or null once it was deleted, the window
    /// closed, or it was edited to another substream: a stopping old session must not report under the new settings.
    /// </summary>
    Camera? CurrentCameraFor(Guid cameraId, string url, bool useUdp)
    {
        if (_closed || _cameras.FirstOrDefault(c => c.Id == cameraId) is not { } camera) return null;
        if (camera.UseUdp != useUdp) return null;
        try
        {
            return StreamUrlBuilder.Build(camera, StreamKind.Sub) == url ? camera : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
