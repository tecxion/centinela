using Centinela.Core;
using Centinela.Desktop.ViewModels;
using Centinela.Media;

namespace Centinela.Desktop.Services.Recording;

/// <summary>Saves a tile's picture at the best resolution available.</summary>
sealed class SnapshotService
{
    /// <summary>
    /// A main-stream tile saves its own frame. Grid tiles show the substream: try to grab one full-resolution frame
    /// from the mainstream instead (up to 3 s, or until it fails authentication or <paramref name="cancelled"/>),
    /// falling back to the substream frame. False when there is no frame yet. UI thread.
    /// </summary>
    public async Task<bool> SaveAsync(TileViewModel tile, string path, Func<bool> cancelled)
    {
        if (tile.Session is not { } session) return false;
        if (tile.Kind == StreamKind.Main) return await session.SaveSnapshotAsync(path);
        var main = new StreamSession(StreamUrlBuilder.Build(tile.Camera, StreamKind.Main), tile.Camera.UseUdp);
        try
        {
            main.SetTargetSize(2, 2);
            main.Start();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && !cancelled() && main.State != SessionState.AuthFailed)
            {
                if (main.Mailbox.Sequence > 0) return await main.SaveSnapshotAsync(path);
                await Task.Delay(100);
            }
            return !cancelled() && await session.SaveSnapshotAsync(path);
        }
        finally
        {
            // Stopping joins the session thread: keep that off the UI thread.
            _ = Task.Run(main.Dispose);
        }
    }
}
