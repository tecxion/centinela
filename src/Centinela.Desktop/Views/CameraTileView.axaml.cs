using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Centinela.Desktop.Video;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

partial class CameraTileView : UserControl
{
    static readonly DataFormat<string> DragFormat = DataFormat.CreateStringApplicationFormat("Centinela.CameraId");
    static readonly IBrush NormalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    static readonly IBrush DropBorder = Brushes.DodgerBlue;
    readonly DispatcherTimer _clickTimer = new();
    readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    Point? _pressAt;
    PointerPressedEventArgs? _pressEvent;
    // The window the tile is placed in; its scaling (DPI) is part of the decode size.
    TopLevel? _topLevel;

    public CameraTileView() => InitializeComponent();

    public CameraTileView(TileViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Video.Session = viewModel.Session;
        Video.FirstFrameShown += () => FirstFrameShown?.Invoke(this);
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            Clicked?.Invoke(this);
        };
        _statsTimer.Tick += (_, _) => viewModel.RefreshStats();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TileViewModel.ShowStats)) UpdateStatsTimer();
        };
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropHover(false));
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SizeChanged += (_, _) => UpdateTargetSize();
        InitActions();
        InitZoom();
    }

    public TileViewModel ViewModel { get; } = null!;

    /// <summary>A single click that did not start a drag, raised once the double-click time has passed.</summary>
    public event Action<CameraTileView>? Clicked;
    public event Action<CameraTileView>? FullscreenRequested;
    public event Action<Guid, Guid>? SwapRequested;
    /// <summary>Raised once, after the first video frame has been painted.</summary>
    public event Action<CameraTileView>? FirstFrameShown;

    /// <summary>Stats refresh only while shown and placed; placing the tile again restarts it.</summary>
    void UpdateStatsTimer()
    {
        if (ViewModel.ShowStats && VisualRoot is not null) _statsTimer.Start();
        else _statsTimer.Stop();
    }

    void UpdateTargetSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = ZoomPanBehavior.DecodeSize(Bounds.Size, scaling, _zoom.Scale);
        ViewModel.SetTargetSize(size.Width, size.Height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            _pressAt = null;
            EndPan(e.Pointer);
            FullscreenRequested?.Invoke(this);
            e.Handled = true;
            return;
        }
        _pressAt = e.GetPosition(this);
        _pressEvent = e;
        BeginPan(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndPan(e.Pointer);
        // _pressAt is cleared when a drag starts or on double-click, so reaching here with it set means a plain
        // click, unless the button was released outside the tile.
        if (_pressAt is null) return;
        _pressAt = null;
        if (!TileClick.ReleasedInside(Bounds.Size, e.GetPosition(this))) return;
        _clickTimer.Interval = TopLevel.GetTopLevel(this)?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse)
            ?? TimeSpan.FromMilliseconds(500);
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // A press released outside the tile is not a click.
        _pressAt = null;
    }

    protected override async void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (PanMove(e)) return;
        if (!ViewModel.Manage || _pressAt is not { } start || _pressEvent is not { } press) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;
        _pressAt = null;
        _clickTimer.Stop();
        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormat, ViewModel.Camera.Id.ToString()));
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
    }

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (ViewModel.Manage && e.DataTransfer.Contains(DragFormat)) SetDropHover(true);
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ViewModel.Manage && e.DataTransfer.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropHover(false);
        if (ViewModel.Manage && e.DataTransfer.TryGetValue(DragFormat) is { } raw && Guid.TryParse(raw, out var source)
            && source != ViewModel.Camera.Id)
            SwapRequested?.Invoke(source, ViewModel.Camera.Id);
    }

    internal void SetDropHover(bool hover)
    {
        Frame.BorderBrush = hover ? DropBorder : NormalBorder;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Placed again (e.g. back from a placeholder or moved to another window): stats and DPI as they are now.
        UpdateStatsTimer();
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        UpdateTargetSize();
    }

    void OnScalingChanged(object? sender, EventArgs e) => UpdateTargetSize();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        _clickTimer.Stop();
        _statsTimer.Stop();
        _zoom.EndPan();
        base.OnDetachedFromVisualTree(e);
    }
}
