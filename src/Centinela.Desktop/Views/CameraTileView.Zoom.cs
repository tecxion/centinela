using Avalonia.Input;
using Centinela.Desktop.Video;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

/// <summary>Digital zoom: wheel towards the pointer, drag to pan while zoomed (<see cref="ZoomPanBehavior"/>).</summary>
partial class CameraTileView
{
    ZoomPanBehavior _zoom = null!;

    void InitZoom()
    {
        _zoom = new ZoomPanBehavior(Video, VideoHost);
        _zoom.Changed += () =>
        {
            ViewModel.ShowZoom(_zoom.IsZoomed, _zoom.Scale);
            UpdateTargetSize();
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            // A tile that stops being big (or only covers its replacement) does not stay zoomed.
            if (e.PropertyName == nameof(TileViewModel.EnableZoom) && !ViewModel.EnableZoom && _zoom.IsZoomed)
                ResetZoom();
        };
    }

    partial void OnResetZoom()
    {
        EndPan();
        _zoom.Reset();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!ViewModel.EnableZoom || ViewModel.Session is null)
        {
            base.OnPointerWheelChanged(e);
            return;
        }
        // Avalonia reports the wheel in notches (WPF: Delta / 120).
        _zoom.WheelAt(e.GetPosition(VideoHost), e.Delta.Y);
        e.Handled = true;
    }

    /// <summary>On a left press: starts a pan if the image is zoomed (the pointer is captured until release).</summary>
    void BeginPan(PointerPressedEventArgs e)
    {
        if (_zoom.BeginPan(e.GetPosition(VideoHost))) e.Pointer.Capture(this);
    }

    /// <summary>Pans while the left button is held; returns false when no pan is in progress.</summary>
    bool PanMove(PointerEventArgs e)
    {
        if (!_zoom.IsPanning) return false;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndPan(e.Pointer);
            return true;
        }
        _zoom.PanTo(e.GetPosition(VideoHost));
        // A pan is not a click; zoomed tiles never start a reorder drag.
        if (_pressAt is { } origin)
        {
            var delta = e.GetPosition(this) - origin;
            if (Math.Abs(delta.X) >= 4 || Math.Abs(delta.Y) >= 4)
            {
                _pressAt = null;
                _clickTimer.Stop();
            }
        }
        return true;
    }

    void EndPan(IPointer? pointer = null)
    {
        if (!_zoom.IsPanning) return;
        _zoom.EndPan();
        if (pointer?.Captured == this) pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _zoom.EndPan();
    }
}
