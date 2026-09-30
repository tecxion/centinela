using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>The main window's state: cameras, settings, chosen layout, stats and the status line.</summary>
/// <remarks>Recording and snapshots are in MainViewModel.Recording.cs.</remarks>
sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _layoutIndex;
    [ObservableProperty] private bool _showStats;
    [ObservableProperty] private string _statusText = "";
    /// <summary>The file or folder the status line shows when clicked; null when the message has none.</summary>
    [ObservableProperty] private string? _statusRevealPath;
    [ObservableProperty] private bool _isEmpty;

    public MainViewModel(CameraRepository cameras, SettingsRepository settings)
    {
        Cameras = cameras;
        Settings = settings;
        Layout = new LayoutController(cameras.All, settings.Settings);
        _layoutIndex = LayoutChoices.IndexFor(settings.Settings);
        _showStats = settings.Settings.ShowStats;
        _isEmpty = cameras.All.Count == 0;
        cameras.SaveFailed += message => Notify(message, null);
        InitRecording();
    }

    public CameraRepository Cameras { get; }
    public SettingsRepository Settings { get; }
    public LayoutController Layout { get; }
    public IReadOnlyList<string> LayoutLabels => LayoutChoices.Labels;

    /// <summary>The grid must be rebuilt (layout, order or featured camera changed).</summary>
    public event Action? ViewChanged;

    partial void OnLayoutIndexChanged(int value)
    {
        LayoutChoices.Apply(Settings.Settings, value);
        Settings.Save();
        ViewChanged?.Invoke();
    }

    partial void OnShowStatsChanged(bool value)
    {
        Settings.Settings.ShowStats = value;
        Settings.Save();
    }

    public void Click(Guid cameraId)
    {
        if (!Layout.Click(cameraId)) return;
        Settings.Save();
        ViewChanged?.Invoke();
    }

    public void Drop(Guid source, Guid target)
    {
        switch (Layout.Drop(source, target))
        {
            case DropOutcome.SettingsChanged:
                Settings.Save();
                break;
            case DropOutcome.OrderChanged:
                Cameras.Save();
                break;
            default:
                return;
        }
        ViewChanged?.Invoke();
    }

    /// <summary>Shows a message in the status line; clicking it then reveals <paramref name="revealPath"/>, if any.</summary>
    public void Notify(string message, string? revealPath)
    {
        StatusText = message;
        StatusRevealPath = revealPath;
    }

    /// <summary>
    /// Deletes the camera (the window has confirmed and retired its tiles): stops its recording with a notice, saves,
    /// forgets it as the featured camera and in «Dos principales», and rebuilds the view.
    /// </summary>
    public void RemoveCamera(Guid cameraId)
    {
        if (Recordings.IsRecording(cameraId)) Recordings.StopWithNotice(cameraId);
        Cameras.Remove(cameraId);
        Cameras.Save();
        var settings = Settings.Settings;
        var changed = settings.FeaturedCameraId == cameraId;
        if (changed) settings.FeaturedCameraId = null;
        changed |= settings.DualCameraIds.Remove(cameraId);
        if (changed) Settings.Save();
        CamerasChanged();
    }

    /// <summary>Call after the cameras changed (count may have).</summary>
    public void CamerasChanged()
    {
        IsEmpty = Cameras.All.Count == 0;
        ViewChanged?.Invoke();
    }
}
