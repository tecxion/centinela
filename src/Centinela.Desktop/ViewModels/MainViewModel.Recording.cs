using Centinela.Core;
using Centinela.Desktop.Services.Recording;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>Recordings (per camera and «Grabar todas») and snapshots; their notices go to the status line.</summary>
sealed partial class MainViewModel
{
    readonly SnapshotService _snapshots = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordAllText))]
    private bool _isAnyRecording;

    /// <summary>Owns every recording; the window shows its <see cref="RecordingController.StatusChanged"/> on the tiles.</summary>
    public RecordingController Recordings { get; } = new();

    public string RecordAllText => IsAnyRecording ? "⏹ Detener todas" : "⏺ Grabar todas";

    void InitRecording()
    {
        Recordings.Notify += Notify;
        Recordings.Failure += message => Notify(message, null);
        Recordings.StatusChanged += (_, _) => IsAnyRecording = Recordings.AnyRecording;
    }

    /// <summary>Uses the current camera with that Id (the tile may hold an older copy); does nothing if it was deleted.</summary>
    public void ToggleRecording(Camera shown)
    {
        if (Cameras.Find(shown.Id) is not { } camera) return;
        if (Recordings.IsRecording(camera.Id)) Recordings.StopWithNotice(camera.Id);
        else Recordings.Start(camera);
    }

    /// <summary>
    /// Stops every recording if any is running; otherwise records every camera (<paramref name="allCameras"/>)
    /// or the cameras on screen.
    /// </summary>
    public void ToggleRecordAll(bool allCameras)
    {
        if (Recordings.AnyRecording)
        {
            Recordings.StopAllWithNotice();
            return;
        }
        var onScreen = Layout.Plan().Tiles.Select(t => t.CameraId);
        var targets = RecordAll.Targets(Cameras.All, onScreen, allCameras);
        foreach (var id in targets)
            if (Cameras.Find(id) is { } camera) Recordings.Start(camera);
    }

    /// <summary>The recordings folder, created if it does not exist yet (to open it).</summary>
    public static string EnsureRecordingsFolder()
    {
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        return AppPaths.RecordingsDirectory;
    }

    /// <summary>Saves the tile's picture and reports it; the tile is busy meanwhile. UI thread.</summary>
    public async Task SnapshotAsync(TileViewModel tile)
    {
        if (tile.SnapshotBusy) return;
        var path = AppPaths.SnapshotFile(tile.Camera.Name, DateTime.Now);
        tile.SnapshotBusy = true;
        try
        {
            var saved = await _snapshots.SaveAsync(tile, path, () => tile.IsShutDown);
            if (tile.IsShutDown) return;
            Notify(saved ? $"Captura guardada: {path}" : "Aún no hay imagen para capturar.", File.Exists(path) ? path : null);
        }
        catch (Exception ex)
        {
            if (!tile.IsShutDown) Notify($"No se pudo guardar la captura: {ex.Message}", null);
        }
        finally
        {
            tile.SnapshotBusy = false;
        }
    }
}
