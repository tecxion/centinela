using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>The main window's state: cameras, settings, chosen layout, stats and the status line.</summary>
sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _layoutIndex;
    [ObservableProperty] private bool _showStats;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isEmpty;

    public MainViewModel(CameraRepository cameras, SettingsRepository settings)
    {
        Cameras = cameras;
        Settings = settings;
        Layout = new LayoutController(cameras.All, settings.Settings);
        _layoutIndex = LayoutChoices.IndexFor(settings.Settings);
        _showStats = settings.Settings.ShowStats;
        _isEmpty = cameras.All.Count == 0;
        cameras.SaveFailed += message => StatusText = message;
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

    /// <summary>Call after the cameras changed (count may have).</summary>
    public void CamerasChanged()
    {
        IsEmpty = Cameras.All.Count == 0;
        ViewChanged?.Invoke();
    }
}
