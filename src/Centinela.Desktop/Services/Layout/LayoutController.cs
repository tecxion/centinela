using Centinela.Core;

namespace Centinela.Desktop.Services.Layout;

/// <summary>
/// Which tile goes where for the current cameras and settings, and what a click or a drop changes. Mutates the
/// settings (featured/dual choice) and the cameras' order; the caller saves and rebuilds the view.
/// </summary>
sealed class LayoutController(IReadOnlyList<Camera> cameras, AppSettings settings)
{
    public bool IsFeaturedLayout => settings.LayoutMode is LayoutMode.Featured or LayoutMode.FeaturedLeft;

    public LayoutPlan Plan()
    {
        var plan = PlanView();
        var bigs = plan.Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToHashSet();
        var tiles = plan.Slots.Select(s =>
        {
            var camera = cameras.First(c => c.Id == s.CameraId);
            return new PlacedTile(s.CameraId, ViewPlanner.StreamFor(camera, s.Kind), bigs.Contains(s.CameraId),
                s.Row, s.Column, s.RowSpan, s.ColumnSpan);
        }).ToList();
        return new LayoutPlan(plan.Rows, plan.Columns, tiles);
    }

    /// <summary>The cameras shown big, in plan order (dual: left, right).</summary>
    public IReadOnlyList<Guid> BigCameraIds() =>
        PlanView().Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToList();

    /// <summary>A click on a tile: features it, or in dual replaces the older big camera. True when something changed.</summary>
    public bool Click(Guid cameraId)
    {
        if (IsFeaturedLayout)
        {
            if (FeaturedCameraId() == cameraId) return false;
            settings.FeaturedCameraId = cameraId;
            return true;
        }
        if (settings.LayoutMode != LayoutMode.Dual) return false;
        var current = new DualState(BigCameraIds(), settings.DualNextReplace);
        var next = DualSelection.Click(current, cameraId);
        if (ReferenceEquals(next, current)) return false;
        Store(next);
        return true;
    }

    /// <summary>A tile dragged onto another one.</summary>
    public DropOutcome Drop(Guid source, Guid target)
    {
        var a = cameras.FirstOrDefault(c => c.Id == source);
        var b = cameras.FirstOrDefault(c => c.Id == target);
        if (a is null || b is null || source == target) return DropOutcome.None;
        if (IsFeaturedLayout && target == FeaturedCameraId())
        {
            // Dropping a thumbnail onto the featured tile features the dragged camera.
            settings.FeaturedCameraId = source;
            return DropOutcome.SettingsChanged;
        }
        if (settings.LayoutMode == LayoutMode.Dual && BigCameraIds() is var bigs && bigs.Contains(target))
        {
            // Dropping onto a big camera puts the dragged camera there (swapping when both are big).
            Store(DualSelection.Drop(new DualState(bigs, settings.DualNextReplace), source, bigs.ToList().IndexOf(target)));
            return DropOutcome.SettingsChanged;
        }
        (a.Order, b.Order) = (b.Order, a.Order);
        return DropOutcome.OrderChanged;
    }

    ViewPlan PlanView() =>
        ViewPlanner.Plan(cameras, settings.LayoutMode, settings.GridMode, settings.FeaturedCameraId, settings.DualCameraIds);

    Guid? FeaturedCameraId() => IsFeaturedLayout && BigCameraIds() is [var first, ..] ? first : null;

    void Store(DualState state)
    {
        settings.DualCameraIds = [.. state.Ids];
        settings.DualNextReplace = state.NextReplace;
    }
}
