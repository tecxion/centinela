using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

/// <summary>The action bar (shown while the pointer is over the tile) and the right-click menu; each raises an event.</summary>
partial class CameraTileView
{
    /// <summary>Switch this camera's big view between main and substream (manage tiles only).</summary>
    public event Action<CameraTileView>? QualityToggleRequested;
    public event Action<CameraTileView>? AudioToggleRequested;
    /// <summary>Turn this camera's motion detection on or off (manage tiles only).</summary>
    public event Action<CameraTileView>? MotionToggleRequested;
    /// <summary>Turn this camera's smoothing on or off (manage tiles only).</summary>
    public event Action<CameraTileView>? SmoothingToggleRequested;
    public event Action<CameraTileView>? SnapshotRequested;
    public event Action<CameraTileView>? RecordRequested;
    public event Action<CameraTileView>? EditRequested;
    public event Action<CameraTileView>? DuplicateRequested;
    /// <summary>«Calidad de la cámara…»: the owner opens the read-only encoder report.</summary>
    public event Action<CameraTileView>? EncoderInfoRequested;
    public event Action<CameraTileView>? DeleteRequested;

    /// <summary>Back to the whole image; the zoom support implements <see cref="OnResetZoom"/>.</summary>
    public void ResetZoom() => OnResetZoom();

    partial void OnResetZoom();

    void InitActions()
    {
        PointerEntered += (_, _) => Actions.IsVisible = true;
        PointerExited += (_, _) => Actions.IsVisible = false;
        ContextMenu = BuildMenu();
    }

    /// <summary>The view-model properties the menu shows; other changes leave it alone.</summary>
    static readonly HashSet<string> MenuProperties =
    [
        nameof(TileViewModel.CanSnapshot), nameof(TileViewModel.HasSession), nameof(TileViewModel.RecordToolTip),
        nameof(TileViewModel.MotionEnabled), nameof(TileViewModel.SmoothingOn), nameof(TileViewModel.IsZoomed),
    ];

    /// <summary>Same items and order as the Windows version; checks and texts follow the view model.</summary>
    ContextMenu BuildMenu()
    {
        var vm = ViewModel;
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
        if (vm.Manage) Item("Pantalla completa", () => FullscreenRequested?.Invoke(this));
        var snapshot = Item("Captura", () => SnapshotRequested?.Invoke(this));
        var record = Item("Grabar", () => RecordRequested?.Invoke(this));
        MenuItem? motion = null, smoothing = null;
        if (vm.Manage)
        {
            Item("Editar…", () => EditRequested?.Invoke(this));
            Item("Duplicar…", () => DuplicateRequested?.Invoke(this));
            Item("Calidad de la cámara…", () => EncoderInfoRequested?.Invoke(this));
            // The check marks are set by the owner (SetMotionEnabled / SetSmoothing); a click only asks for the toggle.
            motion = Item("Detección de movimiento", () => MotionToggleRequested?.Invoke(this));
            motion.ToggleType = MenuItemToggleType.CheckBox;
            smoothing = Item("Suavizar imagen (1–2 s de retraso)", () => SmoothingToggleRequested?.Invoke(this));
            smoothing.ToggleType = MenuItemToggleType.CheckBox;
            ToolTip.SetTip(smoothing, "Para cámaras con wifi irregular: la imagen va con retraso pero sin tirones.");
        }
        // Shown only while the tile is zoomed.
        var resetZoom = Item("Restablecer zoom", ResetZoom);
        if (vm.Manage)
        {
            menu.Items.Add(new Separator());
            Item("Eliminar", () => DeleteRequested?.Invoke(this));
        }

        void Sync()
        {
            snapshot.IsEnabled = vm.CanSnapshot;
            record.IsEnabled = vm.HasSession;
            record.Header = vm.RecordToolTip;
            if (motion is not null) motion.IsChecked = vm.MotionEnabled;
            if (smoothing is not null) smoothing.IsChecked = vm.SmoothingOn;
            resetZoom.IsVisible = vm.IsZoomed;
        }
        // Also on opening: a check item may have toggled itself on the last click, but the owner decides.
        menu.Opening += (_, _) => Sync();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || MenuProperties.Contains(e.PropertyName)) Sync();
        };
        Sync();
        return menu;
    }

    void Quality_Click(object? sender, RoutedEventArgs e) => QualityToggleRequested?.Invoke(this);
    void Audio_Click(object? sender, RoutedEventArgs e) => AudioToggleRequested?.Invoke(this);
    void Motion_Click(object? sender, RoutedEventArgs e) => MotionToggleRequested?.Invoke(this);
    void Snapshot_Click(object? sender, RoutedEventArgs e) => SnapshotRequested?.Invoke(this);
    void Record_Click(object? sender, RoutedEventArgs e) => RecordRequested?.Invoke(this);
    void Edit_Click(object? sender, RoutedEventArgs e) => EditRequested?.Invoke(this);
    void Delete_Click(object? sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this);
}
