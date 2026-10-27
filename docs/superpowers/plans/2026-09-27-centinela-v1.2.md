# Centinela v1.2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two new layouts (mirrored L, two big + thumbnails), a Help menu with an About box and GitHub update checks, a per-camera context menu with Duplicate and a better connection test, digital zoom, and single-camera audio with mute.

**Architecture:** Pure logic (layout geometry, dual selection, update check/policy, zoom maths, audio arbitration) lives in `Centinela.Core` with xUnit tests. `Centinela.Media` gains stream info (resolution/codecs) and an audio pump (decode + resample on its own thread, pushed to an `IAudioSink`). `Centinela.App` wires them into WPF: layout selector, menus, windows, tile context menu, zoom transform and a NAudio WASAPI output.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF (+WinForms for the tray), FFmpeg.AutoGen 9.0.1.1 over FFmpeg n9.0 LGPL shared DLLs, NAudio.Wasapi 2.2.1, xUnit 2 + Xunit.SkippableFact, mediamtx for RTSP integration tests.

**Spec:** `docs/superpowers/specs/2026-09-27-v1.2-design.md`

## Global Constraints

- .NET 10, WPF, FFmpeg.AutoGen 9.0.1.1; FFmpeg BtbN n9.0 LGPL shared (unchanged).
- Only new dependency: `NAudio.Wasapi` (MIT), pinned `2.2.1`, in `src/Centinela.App/Centinela.App.csproj` only. Listed in `THIRD-PARTY-NOTICES.md`.
- App version `<Version>1.2.0</Version>` in `Directory.Build.props`. GitHub tags `v<major>.<minor>.<patch>`.
- User-visible text in Spanish; no translation resources.
- Passwords never in plain text in logs, toasts, UI, or the GitHub request.
- The GitHub request sends no user data: only `User-Agent: Centinela/<version>` and `Accept: application/vnd.github+json`.
- Tests never touch real data (`CENTINELA_DATA_DIR`, the HKCU Run key) and never call the real GitHub (fake `HttpMessageHandler`).
- Nothing added may buffer video. Audio is separate and is dropped when it lags (max 150 ms queued).
- Repository: `tecxion/centinela`, page `https://github.com/tecxion/centinela`.
- Do not touch the user's running app or its data; build the App into a scratch output (`-p:BaseOutputPath=<scratch>\`) if `src/Centinela.App/bin` is locked.
- `git add` explicit paths only. Commit messages end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test commands: `dotnet test tests/Centinela.Core.Tests` and `dotnet test tests/Centinela.Media.Tests` (Media needs `tools/bin/mediamtx.exe` and `ffmpeg` with libx264 on PATH; tests skip otherwise).

## File Structure

Core (`src/Centinela.Core`):
- `Settings.cs` — modify: `LayoutMode` values, dual/update settings, validation.
- `Camera.cs` — modify: `Duplicate()`.
- `GridLayout.cs` — modify: mirrored featured, `ComputeDual`.
- `ViewPlanner.cs` — modify: `FeaturedLeft`, `Dual`.
- `DualSelection.cs` — create: click/drop rule for the two big cameras.
- `Updates/UpdateChecker.cs` — create: GitHub latest-release check.
- `Updates/UpdatePolicy.cs` — create: when to check / notify.
- `ZoomState.cs` — create: zoom maths.
- `AudioCoordinator.cs` — create: at most one audio source.

Media (`src/Centinela.Media`):
- `StreamInfo.cs` — create: resolution + codecs + display text.
- `IAudioSink.cs` — create.
- `AudioPump.cs` — create: audio decode/resample thread.
- `StreamSession.cs` — modify: info, audio stream, sink.

App (`src/Centinela.App`):
- `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.View.cs`, `MainWindow.Menu.cs` — modify.
- `MainWindow.Updates.cs`, `MainWindow.Audio.cs` — create.
- `AppInfo.cs`, `AboutWindow.xaml(.cs)`, `UpdateWindow.xaml(.cs)`, `AudioOutput.cs` — create.
- `Toast.cs`, `ToastHost.xaml.cs`, `ErrorCenter.cs` — modify (toast styles + action).
- `CameraTile.xaml(.cs)` — modify (context menu, zoom, audio button).
- `FullscreenWindow.xaml.cs` — modify (zoom key, audio).
- `AddCameraDialog.xaml(.cs)` — modify (two-stream test).
- `InfoDocuments.cs` — modify (manual text).
- `Centinela.App.csproj` — modify (NAudio, png resource).

Tests: `tests/Centinela.Core.Tests/*` new files per Core unit; `tests/Centinela.Media.Tests/Rtsp/RtspTestServer.cs` (new `av` path), `StreamInfoTests.cs`, `AudioTests.cs`.

Docs: `README.md`, `THIRD-PARTY-NOTICES.md`.

---

### Task 1: Core settings, version and Camera.Duplicate

**Files:**
- Modify: `Directory.Build.props`
- Modify: `src/Centinela.Core/Settings.cs`
- Modify: `src/Centinela.Core/Camera.cs`
- Test: `tests/Centinela.Core.Tests/SettingsStoreTests.cs`, `tests/Centinela.Core.Tests/CameraDuplicateTests.cs` (create)

**Interfaces:**
- Produces: `LayoutMode { Grid = 0, Featured = 1, FeaturedLeft = 2, Dual = 3 }`; `AppSettings.DualCameraIds: List<Guid>`, `DualNextReplace: int`, `CheckUpdatesOnStartup: bool = true`, `LastUpdateCheck: DateTimeOffset?`, `SkippedVersion: string?`; `Camera.Duplicate(): Camera`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Centinela.Core.Tests/SettingsStoreTests.cs` (inside the class; it already has `_dir` and cleanup — reuse its pattern for the file path):

```csharp
    [Fact]
    public void New_settings_have_v12_defaults()
    {
        var s = new SettingsStore(Path.Combine(_dir, "none.json")).Load();
        Assert.True(s.CheckUpdatesOnStartup);
        Assert.Null(s.LastUpdateCheck);
        Assert.Null(s.SkippedVersion);
        Assert.Empty(s.DualCameraIds);
        Assert.Equal(0, s.DualNextReplace);
    }

    [Fact]
    public void Out_of_range_dual_values_are_repaired_on_load()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "s.json");
        File.WriteAllText(path, """{ "layoutMode": "Dual", "dualCameraIds": null, "dualNextReplace": 7 }""");
        var s = new SettingsStore(path).Load();
        Assert.Equal(LayoutMode.Dual, s.LayoutMode);
        Assert.NotNull(s.DualCameraIds);
        Assert.Equal(0, s.DualNextReplace);
    }

    [Fact]
    public void Update_and_dual_settings_round_trip()
    {
        var path = Path.Combine(_dir, "rt.json");
        var id = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.FromHours(2));
        new SettingsStore(path).Save(new AppSettings
        {
            LayoutMode = LayoutMode.FeaturedLeft, DualCameraIds = [id], DualNextReplace = 1,
            CheckUpdatesOnStartup = false, LastUpdateCheck = when, SkippedVersion = "1.3.0",
        });
        var s = new SettingsStore(path).Load();
        Assert.Equal(LayoutMode.FeaturedLeft, s.LayoutMode);
        Assert.Equal([id], s.DualCameraIds);
        Assert.Equal(1, s.DualNextReplace);
        Assert.False(s.CheckUpdatesOnStartup);
        Assert.Equal(when, s.LastUpdateCheck);
        Assert.Equal("1.3.0", s.SkippedVersion);
    }
```

Create `tests/Centinela.Core.Tests/CameraDuplicateTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class CameraDuplicateTests
{
    [Fact]
    public void Duplicate_gets_new_id_and_copy_suffix_and_keeps_everything_else()
    {
        var original = new Camera
        {
            Name = "Garaje", Brand = Brand.Imou, Host = "192.168.1.20", Port = 554, User = "admin",
            Password = "secreto", MainUrlOverride = "rtsp://h/main", SubUrlOverride = "rtsp://h/sub", UseUdp = true, Order = 3,
        };
        var copy = original.Duplicate();
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Garaje (copia)", copy.Name);
        Assert.Equal(("192.168.1.20", 554, "admin", "secreto"), (copy.Host, copy.Port, copy.User, copy.Password));
        Assert.Equal((Brand.Imou, "rtsp://h/main", "rtsp://h/sub", true), (copy.Brand, copy.MainUrlOverride, copy.SubUrlOverride, copy.UseUdp));
        Assert.Equal("Garaje", original.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Core.Tests`
Expected: build errors (`CheckUpdatesOnStartup`, `FeaturedLeft`, `Duplicate` not defined).

- [ ] **Step 3: Implement**

`Directory.Build.props` — add inside the `PropertyGroup`:

```xml
    <Version>1.2.0</Version>
```

`src/Centinela.Core/Settings.cs` — replace the enum and add properties/validation:

```csharp
/// <summary>Stored by name in settings.json; Featured keeps its v1.1 meaning (thumbnails on the right).</summary>
public enum LayoutMode { Grid = 0, Featured = 1, FeaturedLeft = 2, Dual = 3 }
```

Add to `AppSettings` after `TrayHintShown`:

```csharp
    /// <summary>The two big cameras of the Dual layout, left then right (may be stale; the planner validates).</summary>
    public List<Guid> DualCameraIds { get; set; } = [];
    /// <summary>Which big camera (0 left, 1 right) a click on a thumbnail replaces next.</summary>
    public int DualNextReplace { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }
```

In `SettingsStore.Load`, after the `LayoutMode` check:

```csharp
        settings.DualCameraIds ??= [];
        if (settings.DualNextReplace is not (0 or 1)) settings.DualNextReplace = 0;
```

`src/Centinela.Core/Camera.cs` — add after `Clone()`:

```csharp
    /// <summary>A copy to add as a new camera: new Id, name with " (copia)", same address and credentials.</summary>
    public Camera Duplicate()
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{Name} (copia)";
        return copy;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Centinela.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props src/Centinela.Core/Settings.cs src/Centinela.Core/Camera.cs tests/Centinela.Core.Tests/SettingsStoreTests.cs tests/Centinela.Core.Tests/CameraDuplicateTests.cs
git commit -m "feat(core): v1.2 settings, layout modes and Camera.Duplicate"
```

---

### Task 2: Layout geometry, planner and dual selection (Core)

**Files:**
- Modify: `src/Centinela.Core/GridLayout.cs`, `src/Centinela.Core/ViewPlanner.cs`
- Create: `src/Centinela.Core/DualSelection.cs`
- Test: `tests/Centinela.Core.Tests/GridLayoutTests.cs`, `tests/Centinela.Core.Tests/ViewPlannerTests.cs`, `tests/Centinela.Core.Tests/DualSelectionTests.cs` (create)

**Interfaces:**
- Consumes: `LayoutMode` from Task 1.
- Produces:
  - `FeaturedLayout(int Size, int FeaturedSpan, int FeaturedColumn, IReadOnlyList<(int Row, int Column)> Slots)`; `GridLayout.ComputeFeatured(int cameraCount, bool mirrored = false)`.
  - `DualLayout(int Rows, int Columns, int BigSpan, IReadOnlyList<(int Row, int Column)> Bigs, IReadOnlyList<(int Row, int Column)> Slots)`; `GridLayout.ComputeDual(int cameraCount)`.
  - `ViewPlanner.Plan(IReadOnlyList<Camera> cameras, LayoutMode mode, GridMode gridMode, Guid? featuredCameraId, IReadOnlyList<Guid>? dualCameraIds = null)`. In `Dual`, the big cameras come first in `Slots`, left then right, with `Kind = Main`.
  - `DualState(IReadOnlyList<Guid> Ids, int NextReplace)`; `DualSelection.Click(DualState, Guid) : DualState`; `DualSelection.Drop(DualState, Guid, int targetIndex) : DualState`.

- [ ] **Step 1: Write the failing tests**

Append to `GridLayoutTests.cs` (inside the class):

```csharp
    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(7)] [InlineData(8)] [InlineData(12)]
    public void Mirrored_featured_puts_thumbnails_on_the_left_column_then_bottom_row(int count)
    {
        var right = GridLayout.ComputeFeatured(count);
        var left = GridLayout.ComputeFeatured(count, mirrored: true);
        Assert.Equal(right.Size, left.Size);
        Assert.Equal(right.FeaturedSpan, left.FeaturedSpan);
        Assert.Equal(0, right.FeaturedColumn);
        Assert.Equal(1, left.FeaturedColumn);
        var k = left.FeaturedSpan;
        var expected = Enumerable.Range(0, k).Select(r => (r, 0))
            .Concat(Enumerable.Range(0, k + 1).Select(c => (k, c))).Take(count - 1).ToArray();
        Assert.Equal(expected, left.Slots.ToArray());
    }

    [Fact]
    public void Mirrored_featured_with_one_camera_is_single_cell()
    {
        var layout = GridLayout.ComputeFeatured(1, mirrored: true);
        Assert.Equal((1, 1, 0), (layout.Size, layout.FeaturedSpan, layout.FeaturedColumn));
        Assert.Empty(layout.Slots);
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(2, 1, 2, 1)]
    [InlineData(3, 3, 4, 2)]
    [InlineData(7, 4, 6, 3)]
    [InlineData(8, 4, 6, 3)]
    [InlineData(9, 4, 4, 2)]
    [InlineData(12, 5, 6, 3)]
    public void Dual_grid_sizes(int count, int rows, int columns, int bigSpan)
    {
        var layout = GridLayout.ComputeDual(count);
        Assert.Equal((rows, columns, bigSpan), (layout.Rows, layout.Columns, layout.BigSpan));
        Assert.Equal(Math.Min(count, 2), layout.Bigs.Count);
        Assert.Equal(Math.Max(0, count - 2), layout.Slots.Count);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
    [InlineData(15)] [InlineData(16)]
    public void Dual_cells_never_overlap_and_stay_inside_the_grid(int count)
    {
        var layout = GridLayout.ComputeDual(count);
        var used = new HashSet<(int, int)>();
        foreach (var (row, column) in layout.Bigs)
            for (var r = row; r < row + layout.BigSpan; r++)
                for (var c = column; c < column + layout.BigSpan; c++)
                {
                    Assert.InRange(r, 0, layout.Rows - 1);
                    Assert.InRange(c, 0, layout.Columns - 1);
                    Assert.True(used.Add((r, c)));
                }
        foreach (var cell in layout.Slots)
        {
            Assert.InRange(cell.Row, 0, layout.Rows - 1);
            Assert.InRange(cell.Column, 0, layout.Columns - 1);
            Assert.True(used.Add(cell));
        }
    }

    [Fact]
    public void Dual_seven_cameras_two_big_on_top_five_thumbnails_in_one_row()
    {
        var layout = GridLayout.ComputeDual(7);
        Assert.Equal([(0, 0), (0, 3)], layout.Bigs.ToArray());
        Assert.Equal([(3, 0), (3, 1), (3, 2), (3, 3), (3, 4)], layout.Slots.ToArray());
    }
```

Append to `ViewPlannerTests.cs` (inside the class; it has a `Cameras(int)` helper returning cameras with `Order` 0..n−1 — reuse it):

```csharp
    [Fact]
    public void FeaturedLeft_puts_the_featured_camera_on_the_right()
    {
        var cams = Cameras(5);
        var plan = ViewPlanner.Plan(cams, LayoutMode.FeaturedLeft, GridMode.Auto, cams[2].Id);
        var big = plan.Slots[0];
        Assert.Equal((cams[2].Id, StreamKind.Main, 0, 1, 2, 2), (big.CameraId, big.Kind, big.Row, big.Column, big.RowSpan, big.ColumnSpan));
        Assert.All(plan.Slots.Skip(1), s => Assert.Equal(StreamKind.Sub, s.Kind));
        Assert.Equal((0, 0), (plan.Slots[1].Row, plan.Slots[1].Column));
    }

    [Fact]
    public void Dual_uses_stored_ids_in_order_as_the_two_big_cameras()
    {
        var cams = Cameras(7);
        var plan = ViewPlanner.Plan(cams, LayoutMode.Dual, GridMode.Auto, null, [cams[5].Id, cams[1].Id]);
        Assert.Equal((4, 6), (plan.Rows, plan.Columns));
        Assert.Equal((cams[5].Id, StreamKind.Main, 0, 0, 3, 3), (plan.Slots[0].CameraId, plan.Slots[0].Kind, plan.Slots[0].Row, plan.Slots[0].Column, plan.Slots[0].RowSpan, plan.Slots[0].ColumnSpan));
        Assert.Equal((cams[1].Id, StreamKind.Main, 0, 3), (plan.Slots[1].CameraId, plan.Slots[1].Kind, plan.Slots[1].Row, plan.Slots[1].Column));
        Assert.Equal([cams[0].Id, cams[2].Id, cams[3].Id, cams[4].Id, cams[6].Id], plan.Slots.Skip(2).Select(s => s.CameraId).ToArray());
        Assert.All(plan.Slots.Skip(2), s => Assert.Equal((StreamKind.Sub, 3, 1, 1), (s.Kind, s.Row, s.RowSpan, s.ColumnSpan)));
    }

    [Fact]
    public void Dual_fills_missing_duplicate_or_unknown_ids_with_the_first_cameras_by_order()
    {
        var cams = Cameras(4);
        var plan = ViewPlanner.Plan(cams, LayoutMode.Dual, GridMode.Auto, null, [cams[3].Id, cams[3].Id, Guid.NewGuid()]);
        Assert.Equal([cams[3].Id, cams[0].Id], plan.Slots.Take(2).Select(s => s.CameraId).ToArray());
        Assert.Equal(4, plan.Slots.Count);
    }

    [Fact]
    public void Dual_with_one_camera_shows_it_big()
    {
        var cams = Cameras(1);
        var plan = ViewPlanner.Plan(cams, LayoutMode.Dual, GridMode.Auto, null, []);
        Assert.Equal((1, 1), (plan.Rows, plan.Columns));
        Assert.Equal(StreamKind.Main, Assert.Single(plan.Slots).Kind);
    }
```

Create `tests/Centinela.Core.Tests/DualSelectionTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class DualSelectionTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact]
    public void Click_replaces_the_oldest_big_and_alternates()
    {
        var s = new DualState([A, B], 0);
        s = DualSelection.Click(s, C);
        Assert.Equal(([C, B], 1), (s.Ids.ToArray(), s.NextReplace));
        s = DualSelection.Click(s, D);
        Assert.Equal(([C, D], 0), (s.Ids.ToArray(), s.NextReplace));
    }

    [Fact]
    public void Click_on_a_big_camera_changes_nothing()
    {
        var s = new DualState([A, B], 1);
        Assert.Same(s, DualSelection.Click(s, A));
    }

    [Fact]
    public void Click_with_fewer_than_two_bigs_appends()
    {
        Assert.Equal([A, C], DualSelection.Click(new DualState([A], 0), C).Ids.ToArray());
    }

    [Fact]
    public void Drop_replaces_the_target_and_next_click_replaces_the_other()
    {
        var s = DualSelection.Drop(new DualState([A, B], 1), C, 1);
        Assert.Equal(([A, C], 0), (s.Ids.ToArray(), s.NextReplace));
    }

    [Fact]
    public void Drop_of_the_other_big_swaps_them()
    {
        var s = DualSelection.Drop(new DualState([A, B], 0), B, 0);
        Assert.Equal(([B, A], 1), (s.Ids.ToArray(), s.NextReplace));
    }

    [Fact]
    public void Drop_on_its_own_place_or_out_of_range_changes_nothing()
    {
        var s = new DualState([A, B], 0);
        Assert.Same(s, DualSelection.Drop(s, A, 0));
        Assert.Same(s, DualSelection.Drop(s, C, 2));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Core.Tests`
Expected: build errors (`mirrored`, `FeaturedColumn`, `ComputeDual`, `DualSelection` not defined).

- [ ] **Step 3: Implement**

`src/Centinela.Core/GridLayout.cs` — replace `FeaturedLayout` and `ComputeFeatured`, add `DualLayout`/`ComputeDual`:

```csharp
public sealed record FeaturedLayout(int Size, int FeaturedSpan, int FeaturedColumn, IReadOnlyList<(int Row, int Column)> Slots);

public sealed record DualLayout(int Rows, int Columns, int BigSpan,
    IReadOnlyList<(int Row, int Column)> Bigs, IReadOnlyList<(int Row, int Column)> Slots);
```

```csharp
    /// <summary>
    /// Featured camera spans k×k cells of a (k+1)×(k+1) grid, k = max(1, ceil((n−2)/2)). The others fill the free
    /// column (right, or left when <paramref name="mirrored"/>) top→bottom, then the bottom row left→right.
    /// </summary>
    public static FeaturedLayout ComputeFeatured(int cameraCount, bool mirrored = false)
    {
        if (cameraCount <= 1) return new FeaturedLayout(1, 1, 0, []);
        var k = Math.Max(1, (int)Math.Ceiling((cameraCount - 2) / 2.0));
        var column = mirrored ? 0 : k;
        var slots = new List<(int Row, int Column)>();
        for (var row = 0; row < k; row++) slots.Add((row, column));
        for (var c = 0; c <= k; c++) slots.Add((k, c));
        return new FeaturedLayout(k + 1, k, mirrored ? 1 : 0, slots.Take(cameraCount - 1).ToList());
    }

    /// <summary>
    /// Two big cameras side by side on top (each BigSpan×BigSpan), thumbnails below in rows of W = 2·BigSpan.
    /// Up to 6 thumbnails fit one row; more use two. W is even and at least 4.
    /// </summary>
    public static DualLayout ComputeDual(int cameraCount)
    {
        if (cameraCount <= 0) return new DualLayout(1, 1, 1, [], []);
        if (cameraCount == 1) return new DualLayout(1, 1, 1, [(0, 0)], []);
        if (cameraCount == 2) return new DualLayout(1, 2, 1, [(0, 0), (0, 1)], []);
        var thumbnails = cameraCount - 2;
        var perRow = thumbnails <= 6 ? thumbnails : (int)Math.Ceiling(thumbnails / 2.0);
        var width = Math.Max(4, perRow + perRow % 2);
        var rows = (int)Math.Ceiling(thumbnails / (double)width);
        var span = width / 2;
        var slots = Enumerable.Range(0, thumbnails).Select(i => (span + i / width, i % width)).ToList();
        return new DualLayout(span + rows, width, span, [(0, 0), (0, span)], slots);
    }
```

Fix any existing test that constructs `FeaturedLayout` positionally (none known; existing tests only read `.Slots`).

`src/Centinela.Core/ViewPlanner.cs` — new signature and branches:

```csharp
    public static ViewPlan Plan(IReadOnlyList<Camera> cameras, LayoutMode mode, GridMode gridMode, Guid? featuredCameraId,
        IReadOnlyList<Guid>? dualCameraIds = null)
    {
        var ordered = cameras.OrderBy(c => c.Order).ToList();
        if (ordered.Count == 0) return new ViewPlan(1, 1, []);

        if (mode is LayoutMode.Featured or LayoutMode.FeaturedLeft)
        {
            var featured = ordered.FirstOrDefault(c => c.Id == featuredCameraId) ?? ordered[0];
            var layout = GridLayout.ComputeFeatured(ordered.Count, mirrored: mode == LayoutMode.FeaturedLeft);
            var slots = new List<TileSlot>
            {
                new(featured.Id, StreamKind.Main, 0, layout.FeaturedColumn, layout.FeaturedSpan, layout.FeaturedSpan),
            };
            slots.AddRange(ordered.Where(c => c.Id != featured.Id).Select((c, i) =>
                new TileSlot(c.Id, StreamKind.Sub, layout.Slots[i].Row, layout.Slots[i].Column, 1, 1)));
            return new ViewPlan(layout.Size, layout.Size, slots);
        }

        if (mode == LayoutMode.Dual)
        {
            var layout = GridLayout.ComputeDual(ordered.Count);
            var known = ordered.Select(c => c.Id).ToHashSet();
            var bigs = (dualCameraIds ?? []).Where(known.Contains).Distinct().Take(2).ToList();
            foreach (var camera in ordered)
            {
                if (bigs.Count >= layout.Bigs.Count) break;
                if (!bigs.Contains(camera.Id)) bigs.Add(camera.Id);
            }
            var slots = bigs.Select((id, i) =>
                new TileSlot(id, StreamKind.Main, layout.Bigs[i].Row, layout.Bigs[i].Column, layout.BigSpan, layout.BigSpan)).ToList();
            slots.AddRange(ordered.Where(c => !bigs.Contains(c.Id)).Select((c, i) =>
                new TileSlot(c.Id, StreamKind.Sub, layout.Slots[i].Row, layout.Slots[i].Column, 1, 1)));
            return new ViewPlan(layout.Rows, layout.Columns, slots);
        }

        var visible = ordered.Take(GridLayout.VisibleCount(ordered.Count, gridMode)).ToList();
        var size = GridLayout.Compute(visible.Count, gridMode);
        return new ViewPlan(size.Rows, size.Columns, visible
            .Select((c, i) => new TileSlot(c.Id, StreamKind.Sub, i / size.Columns, i % size.Columns, 1, 1))
            .ToList());
    }
```

Create `src/Centinela.Core/DualSelection.cs`:

```csharp
namespace Centinela.Core;

/// <summary>The two big cameras of the Dual layout (left, right) and which one a click replaces next.</summary>
public sealed record DualState(IReadOnlyList<Guid> Ids, int NextReplace);

public static class DualSelection
{
    /// <summary>A click on a thumbnail replaces the big camera that has gone longest without changing.</summary>
    public static DualState Click(DualState state, Guid cameraId)
    {
        if (state.Ids.Contains(cameraId)) return state;
        if (state.Ids.Count < 2) return new DualState([.. state.Ids, cameraId], state.NextReplace);
        var ids = state.Ids.ToList();
        ids[state.NextReplace] = cameraId;
        return new DualState(ids, 1 - state.NextReplace);
    }

    /// <summary>A drop onto big camera <paramref name="targetIndex"/> puts the camera there (swapping if it was the other big one).</summary>
    public static DualState Drop(DualState state, Guid cameraId, int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= state.Ids.Count || state.Ids[targetIndex] == cameraId) return state;
        var ids = state.Ids.ToList();
        var from = ids.IndexOf(cameraId);
        if (from >= 0) (ids[from], ids[targetIndex]) = (ids[targetIndex], ids[from]);
        else ids[targetIndex] = cameraId;
        return new DualState(ids, 1 - targetIndex);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Centinela.Core.Tests`
Expected: all pass (existing featured/planner tests unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Core/GridLayout.cs src/Centinela.Core/ViewPlanner.cs src/Centinela.Core/DualSelection.cs tests/Centinela.Core.Tests/GridLayoutTests.cs tests/Centinela.Core.Tests/ViewPlannerTests.cs tests/Centinela.Core.Tests/DualSelectionTests.cs
git commit -m "feat(core): mirrored featured and dual layouts with dual selection rule"
```

---

### Task 3: New layouts in the window (App)

**Files:**
- Modify: `src/Centinela.App/MainWindow.xaml` (layout combo items)
- Modify: `src/Centinela.App/MainWindow.xaml.cs` (combo index mapping, `SwapCameras`, `DeleteCamera`)
- Modify: `src/Centinela.App/MainWindow.View.cs` (`PlanView`, big-camera helpers, click handling)

**Interfaces:**
- Consumes: Task 1 settings, Task 2 `ViewPlanner.Plan(..., dualCameraIds)`, `DualSelection`, `DualState`.
- Produces: `MainWindow.BigCameraIds(): IReadOnlyList<Guid>` (Main slots of the current plan, in plan order) used by later tasks (audio/zoom only apply to `Main` tiles, which already means big).

No automated tests (App has none); verify by building and by the manual checks in Step 4.

- [ ] **Step 1: Combo items and mapping**

In `MainWindow.xaml` replace the single `"Principal + miniaturas"` item with three:

```xml
                    <ComboBoxItem Content="Principal + miniaturas (derecha)"/>
                    <ComboBoxItem Content="Principal + miniaturas (izquierda)"/>
                    <ComboBoxItem Content="Dos principales + miniaturas"/>
```

In `MainWindow.View.cs` replace `const int FeaturedIndex = 5;` with a mapping used both ways:

```csharp
    // Combo indexes 0..4 are the grid modes; the rest are these layouts, in combo order.
    static readonly LayoutMode[] LayoutModes = [LayoutMode.Featured, LayoutMode.FeaturedLeft, LayoutMode.Dual];

    int ComboIndexForSettings() =>
        _settings.LayoutMode == LayoutMode.Grid
            ? Math.Max(0, Array.IndexOf(GridModes, _settings.GridMode))
            : GridModes.Length + Array.IndexOf(LayoutModes, _settings.LayoutMode);
```

In the constructor use `GridModeBox.SelectedIndex = ComboIndexForSettings();`. `GridMode_Changed` becomes:

```csharp
    void GridMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        var index = GridModeBox.SelectedIndex;
        if (index >= GridModes.Length) _settings.LayoutMode = LayoutModes[index - GridModes.Length];
        else
        {
            _settings.LayoutMode = LayoutMode.Grid;
            _settings.GridMode = GridModes[index];
        }
        SaveSettingsQuietly();
        RebuildView();
    }
```

- [ ] **Step 2: Planning and clicks**

In `MainWindow.View.cs`:

```csharp
    ViewPlan PlanView() =>
        ViewPlanner.Plan(_cameras, _settings.LayoutMode, _settings.GridMode, _settings.FeaturedCameraId, _settings.DualCameraIds);

    bool IsFeaturedLayout => _settings.LayoutMode is LayoutMode.Featured or LayoutMode.FeaturedLeft;

    /// <summary>The cameras shown big (Main) in the current layout, in plan order (dual: left, right).</summary>
    IReadOnlyList<Guid> BigCameraIds() =>
        PlanView().Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToList();

    /// <summary>The camera shown large in a featured layout (null otherwise or without cameras).</summary>
    Guid? FeaturedCameraId() => IsFeaturedLayout && BigCameraIds() is [var first, ..] ? first : null;

    void FeatureCamera(CameraTile tile)
    {
        if (IsFeaturedLayout)
        {
            if (tile.Camera.Id == FeaturedCameraId()) return;
            _settings.FeaturedCameraId = tile.Camera.Id;
        }
        else if (_settings.LayoutMode == LayoutMode.Dual)
        {
            var current = new DualState(BigCameraIds(), _settings.DualNextReplace);
            var next = DualSelection.Click(current, tile.Camera.Id);
            if (ReferenceEquals(next, current)) return;
            StoreDual(next);
        }
        else return;
        SaveSettingsQuietly();
        RebuildView();
    }

    void StoreDual(DualState state)
    {
        _settings.DualCameraIds = [.. state.Ids];
        _settings.DualNextReplace = state.NextReplace;
    }
```

In `MainWindow.xaml.cs` `SwapCameras`, before the order swap:

```csharp
        if (IsFeaturedLayout && target == FeaturedCameraId())
        {
            _settings.FeaturedCameraId = source;
            SaveSettingsQuietly();
            RebuildView();
            return;
        }
        if (_settings.LayoutMode == LayoutMode.Dual && BigCameraIds() is var bigs && bigs.Contains(target))
        {
            // Dropping onto a big camera puts the dragged camera there (swapping when both are big).
            StoreDual(DualSelection.Drop(new DualState(bigs, _settings.DualNextReplace), source, bigs.ToList().IndexOf(target)));
            SaveSettingsQuietly();
            RebuildView();
            return;
        }
```

(Replace the existing `LayoutMode.Featured` check with the `IsFeaturedLayout` one above.)

In `DeleteCamera`, after clearing `FeaturedCameraId`, also drop the id from the dual list:

```csharp
        if (_settings.DualCameraIds.Remove(tile.Camera.Id)) SaveSettingsQuietly();
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Centinela.App -p:BaseOutputPath=<scratch>\bin\ -v q`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Smoke run (isolated data)**

```powershell
$env:CENTINELA_DATA_DIR = "$env:TEMP\centinela-v12-smoke"
& "<scratch>\bin\Debug\net10.0-windows\Centinela.exe"
```

With the test cameras (add two or three `rtsp://127.0.0.1:18554/open` custom cameras if mediamtx is running, else any placeholder URLs), check: each of the 3 new combo entries renders; selection survives restart; in Dual a click on a thumbnail replaces left, then right, alternately; a drop onto a big one replaces it. Close the app with Archivo › Salir.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.App/MainWindow.xaml src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.View.cs
git commit -m "feat(app): mirrored featured and dual layouts in the view selector"
```

---

### Task 4: Ayuda menu and "Acerca de Centinela" (App)

**Files:**
- Create: `src/Centinela.App/AppInfo.cs`, `src/Centinela.App/AboutWindow.xaml`, `src/Centinela.App/AboutWindow.xaml.cs`
- Modify: `src/Centinela.App/MainWindow.xaml`, `src/Centinela.App/MainWindow.Menu.cs`, `src/Centinela.App/Centinela.App.csproj`

**Interfaces:**
- Produces: `AppInfo.Version : Version` (3 components), `AppInfo.RepositoryUrl = "https://github.com/tecxion/centinela"`, `AppInfo.Repository = "tecxion/centinela"`. `MainWindow.xaml` gets a `HelpMenu` `MenuItem` (`x:Name="HelpMenu"`) that Task 6 extends.

- [ ] **Step 1: AppInfo**

```csharp
using System.Reflection;

namespace Centinela.App;

static class AppInfo
{
    public const string Repository = "tecxion/centinela";
    public const string RepositoryUrl = "https://github.com/" + Repository;

    /// <summary>The app version from Directory.Build.props, always major.minor.patch.</summary>
    public static Version Version { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
```

(Task 5 adds `UpdateChecker.Normalize` in Core; after Task 5 you may switch to it, not required.)

- [ ] **Step 2: Menu**

In `MainWindow.xaml` the Archivo menu keeps: Importar JSON…, Exportar JSON…, Carpeta de copia automática…, separator, Salir. Remove Manual/Licencia/Soporte from it and add after the Ver menu:

```xml
            <MenuItem x:Name="HelpMenu" Header="A_yuda">
                <MenuItem Header="_Manual" Click="Manual_Click"/>
                <MenuItem Header="_Licencia" Click="License_Click"/>
                <MenuItem Header="_Soporte" Click="Support_Click"/>
                <Separator/>
                <MenuItem Header="_Acerca de Centinela" Click="About_Click"/>
            </MenuItem>
```

In `MainWindow.Menu.cs` update the class summary to "The Archivo and Ayuda menus" and add:

```csharp
    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
```

- [ ] **Step 3: About window**

In `Centinela.App.csproj` add next to the `.ico` resource: `<Resource Include="Assets\centinela.png" />`.

`AboutWindow.xaml`:

```xml
<Window x:Class="Centinela.App.AboutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Acerca de Centinela" SizeToContent="WidthAndHeight" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False">
    <StackPanel Margin="24" MinWidth="320">
        <Image Source="/Assets/centinela.png" Width="96" Height="96" HorizontalAlignment="Center"
               RenderOptions.BitmapScalingMode="HighQuality"/>
        <TextBlock x:Name="VersionText" FontSize="18" FontWeight="SemiBold" HorizontalAlignment="Center" Margin="0,12,0,0"/>
        <TextBlock Text="Visor de cámaras IP" HorizontalAlignment="Center" Foreground="Gray"/>
        <TextBlock Text="Licencia MIT" HorizontalAlignment="Center" Margin="0,12,0,0"/>
        <TextBlock HorizontalAlignment="Center" Margin="0,4,0,0">
            <Hyperlink x:Name="RepoLink" Click="Link_Click"><Run x:Name="RepoText"/></Hyperlink>
        </TextBlock>
        <TextBlock HorizontalAlignment="Center" Margin="0,4,0,0">
            <Hyperlink x:Name="SupportLink" Click="Link_Click"><Run Text="Soporte: www.tecxart.es"/></Hyperlink>
        </TextBlock>
        <Button Content="Cerrar" Width="90" Margin="0,16,0,0" HorizontalAlignment="Center" IsCancel="True" IsDefault="True"/>
    </StackPanel>
</Window>
```

`AboutWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Centinela.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Centinela {AppInfo.Version}";
        RepoText.Text = AppInfo.RepositoryUrl.Replace("https://", "");
        RepoLink.NavigateUri = new Uri(AppInfo.RepositoryUrl);
        SupportLink.NavigateUri = new Uri(InfoDocuments.WebsiteUri);
    }

    void Link_Click(object sender, RoutedEventArgs e)
    {
        var uri = ((Hyperlink)sender).NavigateUri.AbsoluteUri;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Win32Exception) { MessageBox.Show(this, $"No se pudo abrir {uri}", Title); }
    }
}
```

- [ ] **Step 4: Build and smoke**

Build as in Task 3 Step 3; run with `CENTINELA_DATA_DIR`; check Archivo has no Manual/Licencia/Soporte, Ayuda has them plus "Acerca de Centinela" showing "Centinela 1.2.0" and the icon.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.App/AppInfo.cs src/Centinela.App/AboutWindow.xaml src/Centinela.App/AboutWindow.xaml.cs src/Centinela.App/MainWindow.xaml src/Centinela.App/MainWindow.Menu.cs src/Centinela.App/Centinela.App.csproj
git commit -m "feat(app): Ayuda menu with Manual, Licencia, Soporte and Acerca de"
```

---

### Task 5: Update checker and policy (Core)

**Files:**
- Create: `src/Centinela.Core/Updates/UpdateChecker.cs`, `src/Centinela.Core/Updates/UpdatePolicy.cs`
- Test: `tests/Centinela.Core.Tests/UpdateCheckerTests.cs`, `tests/Centinela.Core.Tests/UpdatePolicyTests.cs`

**Interfaces:**
- Consumes: `AppSettings` (Task 1).
- Produces:
  - `enum UpdateStatus { UpToDate, UpdateAvailable, NoReleases, Failed }`
  - `record UpdateResult(UpdateStatus Status, Version? Latest = null, string? Title = null, string? Notes = null, string? Url = null, DateTimeOffset? PublishedAt = null, string? Error = null)`
  - `class UpdateChecker(HttpClient http, string repository = UpdateChecker.DefaultRepository, TimeSpan? timeout = null)` (timeout defaults to 10 s) with `Task<UpdateResult> CheckAsync(Version current, CancellationToken ct = default)`, `static Version? ParseTag(string?)`, `static Version Normalize(Version)`, `string ReleasesPage`.
  - `static class UpdatePolicy` with `ShouldAutoCheck(AppSettings, DateTimeOffset now)`, `ShouldNotify(UpdateResult, AppSettings)`, `CountsAsChecked(UpdateResult)`.

- [ ] **Step 1: Write the failing tests**

`tests/Centinela.Core.Tests/UpdateCheckerTests.cs`:

```csharp
using System.Net;
using System.Text;
using Centinela.Core;

namespace Centinela.Core.Tests;

public class UpdateCheckerTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return respond(request, ct);
        }
    }

    static readonly Version Current = new(1, 2, 0);

    static (UpdateChecker, FakeHandler) With(HttpStatusCode code, string body = "")
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        return (new UpdateChecker(new HttpClient(handler)), handler);
    }

    static string Release(string tag, string url = "https://github.com/tecxion/centinela/releases/tag/v1.3.0") => $$"""
        { "tag_name": "{{tag}}", "name": "Centinela {{tag}}", "body": "- Novedades", "html_url": "{{url}}",
          "published_at": "2026-10-01T12:00:00Z" }
        """;

    [Fact]
    public async Task Newer_release_is_available_with_details()
    {
        var (checker, handler) = With(HttpStatusCode.OK, Release("v1.3.0"));
        var r = await checker.CheckAsync(Current);
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Equal(new Version(1, 3, 0), r.Latest);
        Assert.Equal("Centinela v1.3.0", r.Title);
        Assert.Equal("- Novedades", r.Notes);
        Assert.Equal("https://github.com/tecxion/centinela/releases/tag/v1.3.0", r.Url);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), r.PublishedAt);
        Assert.Equal("https://api.github.com/repos/tecxion/centinela/releases/latest", handler.Last!.RequestUri!.ToString());
        Assert.Equal("Centinela/1.2.0", handler.Last.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", handler.Last.Headers.Accept.ToString());
        Assert.Null(handler.Last.Headers.Authorization);
    }

    [Theory]
    [InlineData("v1.2.0")] [InlineData("1.2")] [InlineData("v1.1.9")]
    public async Task Same_or_older_release_is_up_to_date(string tag)
    {
        var (checker, _) = With(HttpStatusCode.OK, Release(tag));
        Assert.Equal(UpdateStatus.UpToDate, (await checker.CheckAsync(Current)).Status);
    }

    [Theory]
    [InlineData("latest")] [InlineData("v1")] [InlineData("v1.2.3.4")] [InlineData("")]
    public async Task Invalid_tag_fails(string tag)
    {
        var (checker, _) = With(HttpStatusCode.OK, Release(tag));
        var r = await checker.CheckAsync(Current);
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.Equal("La versión publicada no tiene un formato válido.", r.Error);
    }

    [Fact]
    public async Task Foreign_html_url_is_replaced_by_the_releases_page()
    {
        var (checker, _) = With(HttpStatusCode.OK, Release("v2.0.0", "https://evil.example/download"));
        Assert.Equal("https://github.com/tecxion/centinela/releases/latest", (await checker.CheckAsync(Current)).Url);
    }

    [Fact]
    public async Task Not_found_means_no_releases()
    {
        var (checker, _) = With(HttpStatusCode.NotFound);
        Assert.Equal(UpdateStatus.NoReleases, (await checker.CheckAsync(Current)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Rate_limit_has_its_own_message(HttpStatusCode code)
    {
        var (checker, _) = With(code);
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "GitHub ha limitado las consultas; prueba más tarde."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Server_error_fails()
    {
        var (checker, _) = With(HttpStatusCode.BadGateway);
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "GitHub no responde (código 502)."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Network_failure_is_no_internet()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("dns"))));
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "Sin conexión a Internet."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Timeout_fails_without_throwing()
    {
        var handler = new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var checker = new UpdateChecker(new HttpClient(handler), timeout: TimeSpan.FromMilliseconds(100));
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "GitHub no responde (tiempo agotado)."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Garbage_json_fails()
    {
        var (checker, _) = With(HttpStatusCode.OK, "<html>");
        Assert.Equal("Respuesta de GitHub no válida.", (await checker.CheckAsync(Current)).Error);
    }

    [Theory]
    [InlineData("v1.3.0", "1.3.0")] [InlineData("V2.0", "2.0.0")] [InlineData(" 1.10.2 ", "1.10.2")]
    public void ParseTag_normalizes(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));
}
```

`tests/Centinela.Core.Tests/UpdatePolicyTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class UpdatePolicyTests
{
    static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Auto_check_once_a_day_when_enabled()
    {
        Assert.True(UpdatePolicy.ShouldAutoCheck(new AppSettings(), Now));
        Assert.False(UpdatePolicy.ShouldAutoCheck(new AppSettings { LastUpdateCheck = Now.AddHours(-23) }, Now));
        Assert.True(UpdatePolicy.ShouldAutoCheck(new AppSettings { LastUpdateCheck = Now.AddHours(-24) }, Now));
        Assert.False(UpdatePolicy.ShouldAutoCheck(new AppSettings { CheckUpdatesOnStartup = false }, Now));
    }

    [Fact]
    public void A_last_check_in_the_future_does_not_block_checks()
    {
        Assert.True(UpdatePolicy.ShouldAutoCheck(new AppSettings { LastUpdateCheck = Now.AddDays(3) }, Now));
    }

    [Fact]
    public void Notify_only_for_available_and_not_skipped()
    {
        var available = new UpdateResult(UpdateStatus.UpdateAvailable, new Version(1, 3, 0));
        Assert.True(UpdatePolicy.ShouldNotify(available, new AppSettings()));
        Assert.False(UpdatePolicy.ShouldNotify(available, new AppSettings { SkippedVersion = "1.3.0" }));
        Assert.True(UpdatePolicy.ShouldNotify(available, new AppSettings { SkippedVersion = "1.2.5" }));
        Assert.False(UpdatePolicy.ShouldNotify(new UpdateResult(UpdateStatus.UpToDate), new AppSettings()));
    }

    [Theory]
    [InlineData(UpdateStatus.UpToDate, true)] [InlineData(UpdateStatus.UpdateAvailable, true)]
    [InlineData(UpdateStatus.NoReleases, true)] [InlineData(UpdateStatus.Failed, false)]
    public void Only_answers_from_GitHub_count_as_checked(UpdateStatus status, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.CountsAsChecked(new UpdateResult(status)));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Core.Tests`
Expected: build errors (types missing).

- [ ] **Step 3: Implement**

`src/Centinela.Core/Updates/UpdateChecker.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centinela.Core;

public enum UpdateStatus { UpToDate, UpdateAvailable, NoReleases, Failed }

public sealed record UpdateResult(UpdateStatus Status, Version? Latest = null, string? Title = null, string? Notes = null,
    string? Url = null, DateTimeOffset? PublishedAt = null, string? Error = null);

/// <summary>Asks GitHub for the latest published release. Sends only the app version as User-Agent; never throws.</summary>
public sealed class UpdateChecker(HttpClient http, string repository = UpdateChecker.DefaultRepository, TimeSpan? timeout = null)
{
    public const string DefaultRepository = "tecxion/centinela";
    readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);

    public string ReleasesPage => $"https://github.com/{repository}/releases/latest";

    public async Task<UpdateResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Centinela", Normalize(current).ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        try
        {
            using var response = await http.SendAsync(request, cts.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateResult(UpdateStatus.NoReleases);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed("GitHub ha limitado las consultas; prueba más tarde.");
            if (!response.IsSuccessStatusCode) return Failed($"GitHub no responde (código {(int)response.StatusCode}).");
            var release = JsonSerializer.Deserialize<Release>(await response.Content.ReadAsStringAsync(cts.Token));
            if (ParseTag(release?.TagName) is not { } latest) return Failed("La versión publicada no tiene un formato válido.");
            var url = release!.HtmlUrl is { } html && html.StartsWith($"https://github.com/{repository}/", StringComparison.OrdinalIgnoreCase)
                ? html : ReleasesPage;
            var status = latest > Normalize(current) ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
            return new UpdateResult(status, latest, release.Name, release.Body, url, release.PublishedAt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("GitHub no responde (tiempo agotado).");
        }
        catch (HttpRequestException)
        {
            return Failed("Sin conexión a Internet.");
        }
        catch (JsonException)
        {
            return Failed("Respuesta de GitHub no válida.");
        }
    }

    /// <summary>"v1.3.0", "1.3" → 1.3.0; anything else (1 or 4 components, text) → null.</summary>
    public static Version? ParseTag(string? tag)
    {
        var text = tag?.Trim() ?? "";
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return Version.TryParse(text, out var v) && v.Revision == -1 ? Normalize(v) : null;
    }

    /// <summary>major.minor.patch, so 1.2 and 1.2.0 compare equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static UpdateResult Failed(string error) => new(UpdateStatus.Failed, Error: error);

    sealed class Release
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    }
}
```

Note: `Version.TryParse("1")` returns false (needs 2+ components), `"1.2.3.4"` has Revision 4 → null. Good.

`src/Centinela.Core/Updates/UpdatePolicy.cs`:

```csharp
namespace Centinela.Core;

public static class UpdatePolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Enabled and not checked in the last 24 h (a last check in the future — clock changed — does not block).</summary>
    public static bool ShouldAutoCheck(AppSettings settings, DateTimeOffset now) =>
        settings.CheckUpdatesOnStartup
        && (settings.LastUpdateCheck is not { } last || last > now || now - last >= Interval);

    public static bool ShouldNotify(UpdateResult result, AppSettings settings) =>
        result is { Status: UpdateStatus.UpdateAvailable, Latest: { } latest } && latest.ToString() != settings.SkippedVersion;

    /// <summary>Only an answer from GitHub resets the daily timer; failures are retried next start.</summary>
    public static bool CountsAsChecked(UpdateResult result) => result.Status != UpdateStatus.Failed;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Centinela.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Core/Updates tests/Centinela.Core.Tests/UpdateCheckerTests.cs tests/Centinela.Core.Tests/UpdatePolicyTests.cs
git commit -m "feat(core): GitHub update checker and daily update policy"
```

---

### Task 6: Update UI, automatic check and actionable toasts (App)

**Files:**
- Modify: `src/Centinela.App/Toast.cs`, `src/Centinela.App/ToastHost.xaml.cs`, `src/Centinela.App/ErrorCenter.cs`
- Create: `src/Centinela.App/UpdateWindow.xaml`, `src/Centinela.App/UpdateWindow.xaml.cs`, `src/Centinela.App/MainWindow.Updates.cs`
- Modify: `src/Centinela.App/MainWindow.xaml` (menu item), `src/Centinela.App/MainWindow.xaml.cs` (call `InitUpdates()`)

**Interfaces:**
- Consumes: `UpdateChecker`, `UpdateResult`, `UpdatePolicy` (Task 5); `AppInfo` (Task 4); `HelpMenu` (Task 4).
- Produces: `enum ToastStyle { Error, Recovery, Info }`; `record Toast(string Title, string Message, ToastStyle Style, string? ActionText = null, Action? OnAction = null)`. Task 11 reuses `ToastStyle.Error`.

- [ ] **Step 1: Toasts**

`Toast.cs`:

```csharp
namespace Centinela.App;

public enum ToastStyle { Error, Recovery, Info }

/// <summary>A notice; with <see cref="OnAction"/>, clicking it runs the action instead of opening the error log.</summary>
public sealed record Toast(string Title, string Message, ToastStyle Style, string? ActionText = null, Action? OnAction = null);
```

`ErrorCenter.cs`: `new Toast(text.Title, text.Advice, false)` → `new Toast(text.Title, text.Advice, ToastStyle.Error)`; recovery → `ToastStyle.Recovery`.

`ToastHost.Show`: background by style (Error `#5A1F1F`, Recovery `#1E4D2B`, Info `#1F3A5A`); lifetime Recovery 4 s, Error 8 s, Info 12 s. If `ActionText` is set, add under the message a `TextBlock` with the action text underlined (`TextDecorations.Underline`, foreground `#9CD0FF`). Card click: `Remove(); if (toast.OnAction is { } action) action(); else OpenLogRequested?.Invoke();`.

- [ ] **Step 2: Update window**

`UpdateWindow.xaml` (≈ 520×420, `WindowStartupLocation="CenterOwner"`, `ShowInTaskbar="False"`):
- `TextBlock x:Name="InstalledText"` ("Versión instalada: 1.2.0")
- `TextBlock x:Name="StatusText" FontWeight="SemiBold" TextWrapping="Wrap"`
- `TextBox x:Name="NotesBox" IsReadOnly="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" Height="200" Visibility="Collapsed"`
- `CheckBox x:Name="AutoCheckBox" Content="Comprobar al arrancar"`
- Buttons right-aligned: `DownloadButton` "Descargar" (collapsed), `SkipButton` "Omitir esta versión" (collapsed), `RetryButton` "Volver a comprobar", "Cerrar" (`IsCancel`).

`UpdateWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Centinela.Core;

namespace Centinela.App;

public partial class UpdateWindow : Window
{
    readonly UpdateChecker _checker;
    readonly AppSettings _settings;
    readonly Action _save;
    UpdateResult? _result;

    /// <param name="initial">A result already obtained (from the automatic check's notice); null checks now.</param>
    public UpdateWindow(UpdateChecker checker, AppSettings settings, Action save, UpdateResult? initial)
    {
        InitializeComponent();
        _checker = checker;
        _settings = settings;
        _save = save;
        InstalledText.Text = $"Versión instalada: {AppInfo.Version}";
        AutoCheckBox.IsChecked = settings.CheckUpdatesOnStartup;
        AutoCheckBox.Click += (_, _) =>
        {
            _settings.CheckUpdatesOnStartup = AutoCheckBox.IsChecked == true;
            _save();
        };
        DownloadButton.Click += (_, _) => Open(_result?.Url ?? _checker.ReleasesPage);
        SkipButton.Click += (_, _) =>
        {
            _settings.SkippedVersion = _result?.Latest?.ToString();
            _save();
            Close();
        };
        RetryButton.Click += async (_, _) => await CheckAsync();
        Loaded += async (_, _) =>
        {
            if (initial is null) await CheckAsync();
            else Show(initial);
        };
    }

    async Task CheckAsync()
    {
        RetryButton.IsEnabled = false;
        StatusText.Text = "Comprobando…";
        NotesBox.Visibility = DownloadButton.Visibility = SkipButton.Visibility = Visibility.Collapsed;
        var result = await _checker.CheckAsync(AppInfo.Version);
        if (!IsLoaded) return;
        if (UpdatePolicy.CountsAsChecked(result))
        {
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            _save();
        }
        Show(result);
        RetryButton.IsEnabled = true;
    }

    void Show(UpdateResult result)
    {
        _result = result;
        var available = result.Status == UpdateStatus.UpdateAvailable;
        StatusText.Text = result.Status switch
        {
            UpdateStatus.UpdateAvailable => $"Hay una versión nueva: {result.Latest}" +
                (result.PublishedAt is { } date ? $" ({date.LocalDateTime:d})" : ""),
            UpdateStatus.UpToDate => "Tienes la última versión.",
            UpdateStatus.NoReleases => "Aún no hay versiones publicadas.",
            _ => $"No se pudo comprobar: {result.Error}",
        };
        NotesBox.Text = result.Notes ?? "";
        NotesBox.Visibility = available && NotesBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Visibility = SkipButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Win32Exception) { MessageBox.Show(this, $"No se pudo abrir {url}", Title); }
    }
}
```

(`Show(UpdateResult)` hides `Window.Show()` — name it `ShowResult` to avoid the warning.)

- [ ] **Step 3: Menu and automatic check**

In `MainWindow.xaml`, inside `HelpMenu` before the "Acerca de" item:

```xml
                <MenuItem Header="_Buscar actualizaciones…" Click="Updates_Click"/>
```

`MainWindow.Updates.cs`:

```csharp
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Centinela.Core;

namespace Centinela.App;

/// <summary>Update checks: Ayuda › Buscar actualizaciones… and a quiet daily check after startup.</summary>
public partial class MainWindow
{
    static readonly HttpClient Http = new();
    readonly UpdateChecker _updates = new(Http, AppInfo.Repository);
    UpdateWindow? _updateWindow;

    void InitUpdates()
    {
        // Test runs never phone home on their own.
        if (AppPaths.IsDataDirectoryOverridden || !UpdatePolicy.ShouldAutoCheck(_settings, DateTimeOffset.Now)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await AutoCheckAsync();
        };
        timer.Start();
    }

    async Task AutoCheckAsync()
    {
        var result = await _updates.CheckAsync(AppInfo.Version);
        if (_closed) return;
        if (UpdatePolicy.CountsAsChecked(result))
        {
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            SaveSettingsQuietly();
        }
        if (result.Status == UpdateStatus.Failed)
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, "Centinela", "Actualizaciones", "No se pudo buscar actualizaciones", result.Error ?? ""));
        if (UpdatePolicy.ShouldNotify(result, _settings))
            ShowToast(new Toast($"Centinela {result.Latest} disponible", result.Title ?? "", ToastStyle.Info, "Ver", () => OpenUpdates(result)));
    }

    void Updates_Click(object sender, RoutedEventArgs e) => OpenUpdates(null);

    void OpenUpdates(UpdateResult? initial)
    {
        if (_updateWindow is { IsLoaded: true })
        {
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new UpdateWindow(_updates, _settings, SaveSettingsQuietly, initial) { Owner = this };
        _updateWindow.Show();
    }
}
```

In the `MainWindow` constructor call `InitUpdates();` after `InitTray();`.

- [ ] **Step 4: Build and smoke**

Build; run with `CENTINELA_DATA_DIR` set. Ayuda › Buscar actualizaciones… shows "Aún no hay versiones publicadas." (no release yet) or the actual state. Toggling "Comprobar al arrancar" persists in `settings.json`. Automatic check must NOT run in this smoke (override set) — confirm nothing in the Registro about updates.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.App/Toast.cs src/Centinela.App/ToastHost.xaml.cs src/Centinela.App/ErrorCenter.cs src/Centinela.App/UpdateWindow.xaml src/Centinela.App/UpdateWindow.xaml.cs src/Centinela.App/MainWindow.Updates.cs src/Centinela.App/MainWindow.xaml src/Centinela.App/MainWindow.xaml.cs
git commit -m "feat(app): update window, daily check and actionable notices"
```

---

### Task 7: Stream info (Media)

**Files:**
- Create: `src/Centinela.Media/StreamInfo.cs`
- Modify: `src/Centinela.Media/StreamSession.cs`
- Modify: `tests/Centinela.Media.Tests/Rtsp/RtspTestServer.cs` (new `av` path)
- Test: `tests/Centinela.Media.Tests/StreamInfoTests.cs` (create)

**Interfaces:**
- Produces:
  - `record StreamInfo(int Width, int Height, string VideoCodec, string? AudioCodec)` (FFmpeg codec names, e.g. `"h264"`, `"hevc"`, `"aac"`) with `string Describe()` → `"2560×1440 · H.265 · audio AAC"` / `"640×360 · H.264 · sin audio"` and `static string DisplayCodec(string name)`.
  - `StreamSession.Info : StreamInfo?` (null until known for the current connection), `event Action<StreamInfo>? InfoAvailable` (session thread, subscriber exceptions isolated, once per connection).
  - Test server path `av` (anonymous read): 640×360 H.264 + 48 kHz AAC sine.
  - Internal for Task 10: the session keeps `int _audioIndex` for the current connection.

- [ ] **Step 1: Write the failing tests**

In `RtspTestServer.cs`: add a read permission for path `av` to the `any` user (next to `open` and `missing`), and publish it in `StartAll` in addition to `open`/`secure`:

```csharp
        _processes.Add(Launch(_ffmpegExe!,
            "-hide_banner -loglevel error -re -f lavfi -i testsrc2=size=640x360:rate=25 " +
            "-re -f lavfi -i sine=frequency=440:sample_rate=48000 " +
            "-c:v libx264 -preset ultrafast -tune zerolatency -g 25 -pix_fmt yuv420p -c:a aac -b:a 64k " +
            $"-f rtsp -rtsp_transport tcp rtsp://127.0.0.1:{Port}/av"));
```

Update the fixture's summary comment to mention `av`.

`tests/Centinela.Media.Tests/StreamInfoTests.cs`:

```csharp
using Centinela.Media.Tests.Rtsp;

namespace Centinela.Media.Tests;

public class StreamInfoDescribeTests
{
    [Theory]
    [InlineData(2560, 1440, "hevc", "aac", "2560×1440 · H.265 · audio AAC")]
    [InlineData(640, 360, "h264", null, "640×360 · H.264 · sin audio")]
    [InlineData(1920, 1080, "h264", "pcm_alaw", "1920×1080 · H.264 · audio G.711 A")]
    [InlineData(1920, 1080, "h264", "pcm_mulaw", "1920×1080 · H.264 · audio G.711 µ")]
    [InlineData(1280, 720, "mjpeg", "opus", "1280×720 · MJPEG · audio OPUS")]
    public void Describe(int w, int h, string video, string? audio, string expected) =>
        Assert.Equal(expected, new StreamInfo(w, h, video, audio).Describe());
}

[Collection("rtsp")]
public sealed class StreamInfoTests(RtspTestServer server)
{
    StreamInfo? InfoOf(string path, bool decode)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        using var session = new StreamSession(server.Url(path), decode: decode);
        StreamInfo? raised = null;
        session.InfoAvailable += info => raised = info;
        session.Start();
        TestUtil.WaitFor(() => raised is not null, TimeSpan.FromSeconds(10));
        Assert.Equal(raised, session.Info);
        return raised;
    }

    [SkippableFact]
    public void Video_only_stream_reports_size_codec_and_no_audio()
    {
        var info = InfoOf("open", decode: true);
        Assert.Equal(new StreamInfo(640, 360, "h264", null), info);
    }

    [SkippableFact]
    public void Stream_with_aac_reports_audio()
    {
        Assert.Equal("aac", InfoOf("av", decode: true)?.AudioCodec);
    }

    [SkippableFact]
    public void Remux_session_reports_info_after_opening()
    {
        Assert.Equal(new StreamInfo(640, 360, "h264", "aac"), InfoOf("av", decode: false));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Media.Tests`
Expected: build errors (`StreamInfo`, `InfoAvailable` missing).

- [ ] **Step 3: Implement**

`src/Centinela.Media/StreamInfo.cs`:

```csharp
namespace Centinela.Media;

/// <summary>What a camera stream carries. Codec names are FFmpeg's (h264, hevc, aac, pcm_alaw…).</summary>
public sealed record StreamInfo(int Width, int Height, string VideoCodec, string? AudioCodec)
{
    public string Describe() =>
        $"{Width}×{Height} · {DisplayCodec(VideoCodec)} · {(AudioCodec is { } a ? $"audio {DisplayCodec(a)}" : "sin audio")}";

    public static string DisplayCodec(string name) => name switch
    {
        "h264" => "H.264",
        "hevc" => "H.265",
        "aac" => "AAC",
        "pcm_alaw" => "G.711 A",
        "pcm_mulaw" => "G.711 µ",
        _ => name.ToUpperInvariant(),
    };
}
```

`StreamSession.cs`:
- Fields: `volatile StreamInfo? _info;`, `bool _infoPublished;` (session thread only), `int _audioIndex = -1;`, `string? _videoCodecName;`, `string? _audioCodecName;`.
- Public: `public StreamInfo? Info => _info;` and `public event Action<StreamInfo>? InfoAvailable;`.
- In `PlayOnce`, at the start reset `_info = null; _infoPublished = false; _audioIndex = -1;`. After `videoIndex`/`stream` are known:

```csharp
            var audio = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, videoIndex, null, 0);
            _audioIndex = audio >= 0 ? audio : -1;
            _videoCodecName = ffmpeg.avcodec_get_name(stream->codecpar->codec_id);
            _audioCodecName = _audioIndex >= 0 ? ffmpeg.avcodec_get_name(fmt->streams[_audioIndex]->codecpar->codec_id) : null;
            // Remux sessions ran avformat_find_stream_info, so the size is known now; decode sessions wait for a frame.
            if (!_decode && stream->codecpar->width > 0) PublishInfo(stream->codecpar->width, stream->codecpar->height);
```

- In `PresentFrame`, after `KeepForSnapshot(src);`: `if (!_infoPublished) PublishInfo(src->width, src->height);`
- Helper:

```csharp
    void PublishInfo(int width, int height)
    {
        _infoPublished = true;
        var info = new StreamInfo(width, height, _videoCodecName ?? "?", _audioCodecName);
        _info = info;
        try { InfoAvailable?.Invoke(info); }
        catch (Exception) { /* isolated like StateChanged */ }
    }
```

Do not clear `_info` when a connection ends (keep last known until the next connection starts; the reset at the start of `PlayOnce` covers that).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Centinela.Media.Tests`
Expected: all pass (62 existing + new).

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Media/StreamInfo.cs src/Centinela.Media/StreamSession.cs tests/Centinela.Media.Tests/Rtsp/RtspTestServer.cs tests/Centinela.Media.Tests/StreamInfoTests.cs
git commit -m "feat(media): stream info with resolution and video/audio codecs"
```

---

### Task 8: Context menu, Duplicar and a two-stream Probar (App)

**Files:**
- Modify: `src/Centinela.App/CameraTile.xaml.cs` (context menu, `DuplicateRequested`, `SnapshotRequested` refactor)
- Modify: `src/Centinela.App/MainWindow.xaml.cs` / `MainWindow.View.cs` (wire duplicate)
- Modify: `src/Centinela.App/AddCameraDialog.xaml`, `src/Centinela.App/AddCameraDialog.xaml.cs`

**Interfaces:**
- Consumes: `Camera.Duplicate()` (Task 1), `StreamInfo`, `StreamSession.InfoAvailable` (Task 7), `ErrorCenter.Translate` (existing).
- Produces: `CameraTile.DuplicateRequested : Action<CameraTile>`; a `ContextMenu` built in `CameraTile` with a `ResetZoomItem` field Task 9 fills in (create the item now, collapsed, no handler — Task 9 wires it).

- [ ] **Step 1: Context menu in the tile**

In `CameraTile` constructor (after `InitializeComponent()`), build the menu in code:

```csharp
        var menu = new ContextMenu();
        MenuItem Item(string header, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            menu.Items.Add(item);
            return item;
        }
        if (manage) Item("Pantalla completa", (_, _) => FullscreenRequested?.Invoke(this));
        _snapshotItem = Item("Captura", Snapshot_Click);
        _recordItem = Item("Grabar", Record_Click);
        if (manage)
        {
            Item("Editar…", Edit_Click);
            Item("Duplicar…", (_, _) => DuplicateRequested?.Invoke(this));
        }
        ResetZoomItem = Item("Restablecer zoom", (_, _) => { });
        ResetZoomItem.Visibility = Visibility.Collapsed;
        if (manage)
        {
            menu.Items.Add(new Separator());
            Item("Eliminar", Delete_Click);
        }
        ContextMenu = menu;
```

Fields: `MenuItem _snapshotItem = null!, _recordItem = null!; internal MenuItem ResetZoomItem { get; private set; } = null!;` and `public event Action<CameraTile>? DuplicateRequested;`.
When the camera has no URL (the early `return` in the constructor) also disable `_snapshotItem`/`_recordItem` (build the menu before that return).
In `SetRecordingStatus` keep `_recordItem.Header` in sync: `"Grabar"` when Off, `"Detener grabación"` otherwise. `Snapshot_Click`'s `SnapshotButton.IsEnabled` toggling also toggles `_snapshotItem.IsEnabled`.
Right-click must not start the click timer or a drag (only left button handlers exist today — no change needed; verify).

- [ ] **Step 2: Duplicar in the window**

In `CreateTile` add `tile.DuplicateRequested += DuplicateCamera;` and in `MainWindow.xaml.cs`:

```csharp
    /// <summary>Opens «Añadir cámara» pre-filled with a copy (new Id, "(copia)"); nothing is saved unless the user saves.</summary>
    void DuplicateCamera(CameraTile tile)
    {
        if (_cameras.FirstOrDefault(c => c.Id == tile.Camera.Id) is { } camera) AddCamera(camera.Duplicate());
    }
```

(`AddCamera` already assigns `Order` at the end and saves only on OK.)

- [ ] **Step 3: Two-stream test in the dialog**

`AddCameraDialog.xaml`: under the preview `Border`, add

```xml
        <TextBlock x:Name="TestResult" TextWrapping="Wrap" Margin="0,4,0,0"/>
```

`AddCameraDialog.xaml.cs`: replace `_test` handling with two sessions and a 10 s timer:

```csharp
    StreamSession? _test;        // substream: decoded, shown in the preview
    StreamSession? _testMain;    // mainstream: not decoded, only for its info
    readonly DispatcherTimer _testTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    string _mainLine = "", _subLine = "";
```

Constructor: `_testTimer.Tick += (_, _) => FinishTest();`.

```csharp
    void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            TestStatus.Text = error;
            return;
        }
        StopTest();
        _mainLine = "Principal: conectando…";
        _subLine = "Secundaria: conectando…";
        ShowTestLines();
        TestStatus.Text = "Conectando…";
        _test = StartTestSession(StreamKind.Sub, decode: true, line => _subLine = line, "Secundaria");
        _testMain = StartTestSession(StreamKind.Main, decode: false, line => _mainLine = line, "Principal");
        _testTimer.Start();
    }

    StreamSession StartTestSession(StreamKind kind, bool decode, Action<string> setLine, string label)
    {
        var session = new StreamSession(StreamUrlBuilder.Build(_camera, kind), _camera.UseUdp, decode);
        if (decode) session.SetTargetSize(640, 360);
        session.InfoAvailable += info => Dispatcher.BeginInvoke(() =>
        {
            if (_test != session && _testMain != session) return;
            setLine($"{label}: {info.Describe()}");
            if (decode) TestStatus.Text = "";
            ShowTestLines();
        });
        session.ErrorOccurred += err => Dispatcher.BeginInvoke(() =>
        {
            if (_test != session && _testMain != session) return;
            setLine($"{label}: {ErrorCenter.Translate(_camera, err.Kind).Short}");
            if (decode) TestStatus.Text = "";
            ShowTestLines();
        });
        session.Start();
        return session;
    }

    void ShowTestLines() => TestResult.Text = $"{_mainLine}\n{_subLine}";

    /// <summary>After 10 s: stop both sessions (the preview keeps its last frame) and mark what never answered.</summary>
    void FinishTest()
    {
        _testTimer.Stop();
        if (_mainLine.EndsWith("conectando…")) _mainLine = "Principal: sin respuesta en 10 s";
        if (_subLine.EndsWith("conectando…")) _subLine = "Secundaria: sin respuesta en 10 s";
        ShowTestLines();
        if (TestStatus.Text == "Conectando…") TestStatus.Text = "";
        foreach (var s in new[] { _test, _testMain })
            if (s is not null) _ = Task.Run(s.Dispose);
        _testMain = null;
        // _test stays referenced so OnRendering keeps showing its last frame; it is already stopping.
    }

    void StopTest()
    {
        _testTimer.Stop();
        foreach (var s in new[] { _test, _testMain })
            if (s is not null) _ = Task.Run(s.Dispose);
        _test = _testMain = null;
        _previewSequence = 0;
    }
```

A session that was already disposed in `FinishTest` and is disposed again by `StopTest` is fine (`Stop` is idempotent). Remove the old `StateChanged`-based status text. Error lines never include `err.Detail`.

- [ ] **Step 4: Build and smoke**

Build; run with `CENTINELA_DATA_DIR`. With mediamtx running (`tools/bin/mediamtx.exe` + an ffmpeg publisher as in the fixture, or run the Media tests' fixture manually), add a custom camera `rtsp://127.0.0.1:18554/open`, press Probar: two lines appear ("Principal: 640×360 · H.264 · sin audio"); with a wrong URL path the line says "Vídeo no disponible". Right-click a tile: menu items work; Duplicar opens a pre-filled dialog named "(copia)".

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.App/CameraTile.xaml.cs src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.View.cs src/Centinela.App/AddCameraDialog.xaml src/Centinela.App/AddCameraDialog.xaml.cs
git commit -m "feat(app): camera context menu, Duplicar and two-stream connection test"
```

---

### Task 9: Digital zoom (Core maths + App)

**Files:**
- Create: `src/Centinela.Core/ZoomState.cs`
- Test: `tests/Centinela.Core.Tests/ZoomStateTests.cs`
- Modify: `src/Centinela.App/CameraTile.xaml`, `src/Centinela.App/CameraTile.xaml.cs`, `src/Centinela.App/FullscreenWindow.xaml.cs`, `src/Centinela.App/MainWindow.xaml.cs`, `src/Centinela.App/MainWindow.View.cs`

**Interfaces:**
- Produces: `ZoomState` with `Scale`, `OffsetX`, `OffsetY`, `IsZoomed`, `Resize(double w, double h)`, `WheelAt(double x, double y, double notches)`, `Pan(double dx, double dy)`, `Reset()`; constants `MinScale = 1`, `MaxScale = 8`, `Step = 1.25`. Screen point = content point × Scale + Offset.
- `CameraTile.EnableZoom : bool` (set by owner: true for `Main` tiles), `CameraTile.ResetZoom()`.

- [ ] **Step 1: Write the failing tests**

`tests/Centinela.Core.Tests/ZoomStateTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class ZoomStateTests
{
    static ZoomState View(double w = 800, double h = 450)
    {
        var z = new ZoomState();
        z.Resize(w, h);
        return z;
    }

    [Fact]
    public void Starts_unzoomed() => Assert.Equal((1.0, 0.0, 0.0, false), (View().Scale, View().OffsetX, View().OffsetY, View().IsZoomed));

    [Fact]
    public void Wheel_keeps_the_point_under_the_cursor_fixed()
    {
        var z = View();
        z.WheelAt(200, 100, 1);
        var contentX = (200 - z.OffsetX) / z.Scale;
        var contentY = (100 - z.OffsetY) / z.Scale;
        Assert.Equal(1.25, z.Scale, 6);
        Assert.Equal(200, contentX, 6);
        Assert.Equal(100, contentY, 6);
    }

    [Fact]
    public void Scale_is_clamped_between_1_and_8()
    {
        var z = View();
        z.WheelAt(400, 225, 50);
        Assert.Equal(8, z.Scale, 6);
        z.WheelAt(400, 225, -100);
        Assert.Equal((1.0, 0.0, 0.0), (z.Scale, z.OffsetX, z.OffsetY));
        Assert.False(z.IsZoomed);
    }

    [Fact]
    public void Pan_is_clamped_so_the_image_always_covers_the_view()
    {
        var z = View();
        z.WheelAt(0, 0, 4); // scale ≈ 2.44, anchored top-left
        z.Pan(500, 500);
        Assert.Equal((0.0, 0.0), (z.OffsetX, z.OffsetY));
        z.Pan(-10_000, -10_000);
        Assert.Equal(800 * (1 - z.Scale), z.OffsetX, 6);
        Assert.Equal(450 * (1 - z.Scale), z.OffsetY, 6);
    }

    [Fact]
    public void Pan_does_nothing_when_not_zoomed()
    {
        var z = View();
        z.Pan(100, 100);
        Assert.Equal((0.0, 0.0), (z.OffsetX, z.OffsetY));
    }

    [Fact]
    public void Resize_reclamps_and_reset_restores()
    {
        var z = View();
        z.WheelAt(800, 450, 6);
        z.Resize(400, 200);
        Assert.InRange(z.OffsetX, 400 * (1 - z.Scale), 0);
        Assert.InRange(z.OffsetY, 200 * (1 - z.Scale), 0);
        z.Reset();
        Assert.Equal((1.0, 0.0, 0.0), (z.Scale, z.OffsetX, z.OffsetY));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Centinela.Core.Tests` — Expected: build error (`ZoomState` missing).

- [ ] **Step 3: Implement ZoomState**

```csharp
namespace Centinela.Core;

/// <summary>Digital zoom of a view: screen = content × Scale + Offset. The image always covers the view.</summary>
public sealed class ZoomState
{
    public const double MinScale = 1, MaxScale = 8, Step = 1.25;
    const double Epsilon = 1e-6;

    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public double ViewWidth { get; private set; }
    public double ViewHeight { get; private set; }
    public bool IsZoomed => Scale > MinScale + Epsilon;

    public void Resize(double width, double height)
    {
        ViewWidth = Math.Max(0, width);
        ViewHeight = Math.Max(0, height);
        Clamp();
    }

    /// <summary>Zooms by <see cref="Step"/> per notch (negative = out) keeping the screen point (x, y) fixed.</summary>
    public void WheelAt(double x, double y, double notches)
    {
        var scale = Math.Clamp(Scale * Math.Pow(Step, notches), MinScale, MaxScale);
        if (scale <= MinScale + Epsilon)
        {
            Reset();
            return;
        }
        var factor = scale / Scale;
        OffsetX = x - (x - OffsetX) * factor;
        OffsetY = y - (y - OffsetY) * factor;
        Scale = scale;
        Clamp();
    }

    public void Pan(double dx, double dy)
    {
        if (!IsZoomed) return;
        OffsetX += dx;
        OffsetY += dy;
        Clamp();
    }

    public void Reset()
    {
        Scale = MinScale;
        OffsetX = OffsetY = 0;
    }

    void Clamp()
    {
        OffsetX = Math.Clamp(OffsetX, ViewWidth * (1 - Scale), 0);
        OffsetY = Math.Clamp(OffsetY, ViewHeight * (1 - Scale), 0);
    }
}
```

Run Core tests — Expected: pass.

- [ ] **Step 4: Zoom in the tile**

`CameraTile.xaml`: wrap the `Image` in `<Grid x:Name="VideoHost" ClipToBounds="True">…</Grid>` and add, bottom-right:

```xml
            <Border x:Name="ZoomBorder" Background="#80000000" Padding="6,1" CornerRadius="4,0,0,0"
                    HorizontalAlignment="Right" VerticalAlignment="Bottom" Visibility="Collapsed">
                <TextBlock x:Name="ZoomLabel" Foreground="White" FontSize="11"/>
            </Border>
```

`CameraTile.xaml.cs`:
- `readonly ZoomState _zoom = new(); Point? _panLast; public bool EnableZoom { get; set; }`
- `OnMouseWheel(MouseWheelEventArgs e)`: if `!EnableZoom || _session is null` → base; else `_zoom.Resize(VideoHost.ActualWidth, VideoHost.ActualHeight); var p = e.GetPosition(VideoHost); _zoom.WheelAt(p.X, p.Y, e.Delta / 120.0); ApplyZoom(); e.Handled = true;`
- `ApplyZoom()`: `Video.RenderTransform = new MatrixTransform(_zoom.Scale, 0, 0, _zoom.Scale, _zoom.OffsetX, _zoom.OffsetY);` `RenderOptions.SetBitmapScalingMode(Video, _zoom.IsZoomed ? BitmapScalingMode.HighQuality : BitmapScalingMode.Linear);` `ZoomBorder.Visibility = _zoom.IsZoomed ? Visible : Collapsed; ZoomLabel.Text = $"{_zoom.Scale:0.#}×";` (Spanish culture gives "2,5×"; format with `CultureInfo.GetCultureInfo("es-ES")`), `ResetZoomItem.Visibility = _zoom.IsZoomed ? Visible : Collapsed;`, then `UpdateTargetSize()`.
- `UpdateTargetSize()` multiplies the box by `_zoom.Scale` so zoomed images are decoded larger (FrameGeometry never upscales beyond native): `(int)(ActualWidth * dpi.DpiScaleX * _zoom.Scale)` etc.
- `SizeChanged` also calls `_zoom.Resize(...)` then `ApplyZoom()` when zoomed.
- `public void ResetZoom() { _zoom.Reset(); ApplyZoom(); }`; `ResetZoomItem.Click += (_, _) => ResetZoom();` (replace Task 8's empty handler).
- Panning: in `OnMouseLeftButtonDown` (single click) when `_zoom.IsZoomed`, set `_panLast = e.GetPosition(this)` and `CaptureMouse()`. In `OnMouseMove`, if `_panLast is { } last && e.LeftButton == Pressed`: `var p = e.GetPosition(this); _zoom.Pan(p.X - last.X, p.Y - last.Y); _panLast = p; ApplyZoom();` and if the movement since the press passed the drag threshold set `_dragStart = null` (no click) — return before the drag-and-drop code so zoomed tiles never start a reorder drag. `OnMouseLeftButtonUp` and `OnLostMouseCapture` clear `_panLast` and `ReleaseMouseCapture()`.
- Double-click (fullscreen) still works when zoomed.

`MainWindow.View.cs` `CreateTile`: `tile.EnableZoom = kind == StreamKind.Main;` (Main tiles exist only in the featured/dual layouts).
`FullscreenWindow`: after creating the tile `_tile.EnableZoom = true;` and in `KeyDown`: `if (e.Key is Key.D0 or Key.NumPad0) _tile.ResetZoom();`.
`MainWindow.xaml.cs` `Window_KeyDown`: add

```csharp
        else if (e.Key is Key.D0 or Key.NumPad0 && TileUnderMouse() is { } tile) tile.ResetZoom();
```

with

```csharp
    static CameraTile? TileUnderMouse()
    {
        for (var node = Mouse.DirectlyOver as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is CameraTile tile) return tile;
        return null;
    }
```

(`Mouse.DirectlyOver` can be a non-visual `Run`; walk with `LogicalTreeHelper.GetParent` when `VisualTreeHelper` does not apply — guard with `node is Visual or Visual3D`.)

- [ ] **Step 5: Build, smoke and commit**

Build and smoke with `CENTINELA_DATA_DIR`: in "Principal + miniaturas" the wheel zooms the big tile toward the cursor up to 8×, "2,5×" label shows, dragging pans (no reorder), `0` and the menu reset; thumbnails do not zoom; fullscreen zooms.

```bash
git add src/Centinela.Core/ZoomState.cs tests/Centinela.Core.Tests/ZoomStateTests.cs src/Centinela.App/CameraTile.xaml src/Centinela.App/CameraTile.xaml.cs src/Centinela.App/FullscreenWindow.xaml.cs src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.View.cs
git commit -m "feat: digital zoom on big tiles and fullscreen"
```

---

### Task 10: Audio decode pipeline (Core coordinator + Media pump)

**Files:**
- Create: `src/Centinela.Core/AudioCoordinator.cs`, `tests/Centinela.Core.Tests/AudioCoordinatorTests.cs`
- Create: `src/Centinela.Media/IAudioSink.cs`, `src/Centinela.Media/AudioPump.cs`
- Modify: `src/Centinela.Media/StreamSession.cs`
- Test: `tests/Centinela.Media.Tests/AudioTests.cs`

**Interfaces:**
- Consumes: `_audioIndex` and the `av` test path (Task 7).
- Produces:
  - `AudioCoordinator<T> where T : class` with `T? Active`, `T? Activate(T source)` (returns the previous active source to silence, or null), `bool Deactivate(T source)`.
  - `interface IAudioSink { void Write(ReadOnlySpan<float> interleavedStereo); }` — 48 kHz, 2 channels, float32; called on the audio thread.
  - `AudioFormat.SampleRate = 48000`, `AudioFormat.Channels = 2` (static class in `IAudioSink.cs`).
  - `StreamSession.SetAudioSink(IAudioSink? sink)` (any thread; no reconnect), `event Action<string>? AudioFailed` (sanitized message; the pump stops decoding for this connection), internal `long AudioFramesDecoded`.

- [ ] **Step 1: Core coordinator (TDD)**

Test:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class AudioCoordinatorTests
{
    sealed class Source;

    [Fact]
    public void Only_one_source_is_active_and_the_previous_is_returned_to_silence()
    {
        var audio = new AudioCoordinator<Source>();
        Source a = new(), b = new();
        Assert.Null(audio.Activate(a));
        Assert.Same(a, audio.Activate(b));
        Assert.Same(b, audio.Active);
        Assert.Null(audio.Activate(b));
    }

    [Fact]
    public void Deactivate_only_affects_the_active_source()
    {
        var audio = new AudioCoordinator<Source>();
        Source a = new(), b = new();
        audio.Activate(a);
        Assert.False(audio.Deactivate(b));
        Assert.Same(a, audio.Active);
        Assert.True(audio.Deactivate(a));
        Assert.Null(audio.Active);
    }
}
```

Implementation:

```csharp
namespace Centinela.Core;

/// <summary>At most one audio source plays in the whole app. Not thread-safe (UI thread).</summary>
public sealed class AudioCoordinator<T> where T : class
{
    public T? Active { get; private set; }

    /// <summary>Makes <paramref name="source"/> the one that plays; returns the source that must now be silenced, if any.</summary>
    public T? Activate(T source)
    {
        if (ReferenceEquals(Active, source)) return null;
        var previous = Active;
        Active = source;
        return previous;
    }

    public bool Deactivate(T source)
    {
        if (!ReferenceEquals(Active, source)) return false;
        Active = null;
        return true;
    }
}
```

Run Core tests — pass.

- [ ] **Step 2: Media tests (failing)**

`tests/Centinela.Media.Tests/AudioTests.cs`:

```csharp
using System.Collections.Concurrent;
using Centinela.Media.Tests.Rtsp;

namespace Centinela.Media.Tests;

[Collection("rtsp")]
public sealed class AudioTests(RtspTestServer server)
{
    sealed class CollectingSink : IAudioSink
    {
        public readonly ConcurrentQueue<int> Chunks = new();
        public long Samples;
        public void Write(ReadOnlySpan<float> samples)
        {
            Chunks.Enqueue(samples.Length);
            Interlocked.Add(ref Samples, samples.Length);
        }
    }

    StreamSession Open(string path)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(server.Url(path));
    }

    [SkippableFact]
    public void With_a_sink_audio_arrives_as_interleaved_stereo_at_48k()
    {
        using var session = Open("av");
        var sink = new CollectingSink();
        session.SetAudioSink(sink);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => Interlocked.Read(ref sink.Samples) >= AudioFormat.SampleRate * AudioFormat.Channels / 2,
            TimeSpan.FromSeconds(10)), "less than 0.5 s of audio in 10 s");
        Assert.All(sink.Chunks, n => Assert.Equal(0, n % AudioFormat.Channels));
    }

    [SkippableFact]
    public void Without_a_sink_audio_is_not_decoded()
    {
        using var session = Open("av");
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        Thread.Sleep(1500);
        Assert.Equal(0, session.AudioFramesDecoded);
    }

    [SkippableFact]
    public void Sink_can_be_attached_and_removed_without_reconnecting()
    {
        using var session = Open("av");
        var states = new ConcurrentQueue<SessionState>();
        session.StateChanged += states.Enqueue;
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
        var sink = new CollectingSink();
        session.SetAudioSink(sink);
        Assert.True(TestUtil.WaitFor(() => Interlocked.Read(ref sink.Samples) > 0, TimeSpan.FromSeconds(5)));
        session.SetAudioSink(null);
        Thread.Sleep(500);
        var after = Interlocked.Read(ref sink.Samples);
        Thread.Sleep(1000);
        Assert.Equal(after, Interlocked.Read(ref sink.Samples));
        Assert.DoesNotContain(SessionState.Reconnecting, states);
    }

    [SkippableFact]
    public void Stream_without_audio_plays_normally_with_a_sink()
    {
        using var session = Open("open");
        session.SetAudioSink(new CollectingSink());
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
    }
}
```

Run: `dotnet test tests/Centinela.Media.Tests` — Expected: build errors.

- [ ] **Step 3: Implement IAudioSink and AudioPump**

`src/Centinela.Media/IAudioSink.cs`:

```csharp
namespace Centinela.Media;

public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
}

/// <summary>Receives decoded audio: interleaved stereo float32 at 48 kHz. Called on the session's audio thread.</summary>
public interface IAudioSink
{
    void Write(ReadOnlySpan<float> interleavedStereo);
}
```

`src/Centinela.Media/AudioPump.cs` — one per connection with an audio stream; owns its own copy of the codec parameters, a bounded packet queue (drop oldest), a background thread, the decoder and the resampler:

```csharp
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>
/// Decodes one connection's audio on its own thread so video never waits. Packets are cloned into a bounded queue
/// (oldest dropped); with no sink attached packets are not queued at all.
/// </summary>
sealed unsafe class AudioPump : IDisposable
{
    const int MaxQueuedPackets = 50;

    readonly Func<IAudioSink?> _sink;
    readonly Action<string> _failed;
    readonly Queue<nint> _queue = new();
    readonly object _lock = new();
    readonly SemaphoreSlim _signal = new(0);
    readonly Thread _thread;
    AVCodecParameters* _parameters;
    volatile bool _stopping;
    volatile bool _broken;

    public AudioPump(AVCodecParameters* source, Func<IAudioSink?> sink, Action<string> failed)
    {
        _sink = sink;
        _failed = failed;
        _parameters = ffmpeg.avcodec_parameters_alloc();
        FFmpegException.ThrowIfError(ffmpeg.avcodec_parameters_copy(_parameters, source), "audio parameters");
        _thread = new Thread(Run) { IsBackground = true, Name = "AudioPump" };
        _thread.Start();
    }

    public long FramesDecoded;

    /// <summary>Session thread. Clones the packet; never blocks.</summary>
    public void Enqueue(AVPacket* packet)
    {
        if (_broken || _stopping) return;
        var clone = ffmpeg.av_packet_clone(packet);
        if (clone is null) return;
        lock (_lock)
        {
            if (_queue.Count >= MaxQueuedPackets)
            {
                var oldest = (AVPacket*)_queue.Dequeue();
                ffmpeg.av_packet_free(&oldest);
            }
            _queue.Enqueue((nint)clone);
        }
        _signal.Release();
    }

    void Run()
    {
        AVCodecContext* dec = null;
        SwrContext* swr = null;
        var frame = ffmpeg.av_frame_alloc();
        float[] buffer = [];
        (int Rate, int Format, ulong Layout) swrInput = default;
        try
        {
            var codec = ffmpeg.avcodec_find_decoder(_parameters->codec_id);
            if (codec is null) throw new InvalidOperationException($"no hay descodificador para {ffmpeg.avcodec_get_name(_parameters->codec_id)}");
            dec = ffmpeg.avcodec_alloc_context3(codec);
            FFmpegException.ThrowIfError(ffmpeg.avcodec_parameters_to_context(dec, _parameters), "audio decoder parameters");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_open2(dec, codec, null), "open audio decoder");

            while (!_stopping)
            {
                _signal.Wait(200);
                while (!_stopping && TryDequeue(out var packet))
                {
                    try
                    {
                        if (_sink() is not { } sink) continue; // detached meanwhile: drop
                        if (ffmpeg.avcodec_send_packet(dec, packet) < 0) continue;
                        while (ffmpeg.avcodec_receive_frame(dec, frame) >= 0)
                        {
                            FramesDecoded++;
                            var input = (frame->sample_rate, frame->format, frame->ch_layout.u.mask ^ (ulong)frame->ch_layout.nb_channels);
                            if (swr is null || input != swrInput)
                            {
                                ffmpeg.swr_free(&swr);
                                AVChannelLayout outLayout;
                                ffmpeg.av_channel_layout_default(&outLayout, AudioFormat.Channels);
                                FFmpegException.ThrowIfError(ffmpeg.swr_alloc_set_opts2(&swr, &outLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT,
                                    AudioFormat.SampleRate, &frame->ch_layout, (AVSampleFormat)frame->format, frame->sample_rate, 0, null), "resampler");
                                FFmpegException.ThrowIfError(ffmpeg.swr_init(swr), "resampler init");
                                swrInput = input;
                            }
                            var capacity = ffmpeg.swr_get_out_samples(swr, frame->nb_samples);
                            if (buffer.Length < capacity * AudioFormat.Channels) buffer = new float[capacity * AudioFormat.Channels];
                            int converted;
                            fixed (float* output = buffer)
                            {
                                var outputs = (byte*)output;
                                converted = ffmpeg.swr_convert(swr, &outputs, capacity, frame->extended_data, frame->nb_samples);
                            }
                            if (converted > 0) sink.Write(buffer.AsSpan(0, converted * AudioFormat.Channels));
                            ffmpeg.av_frame_unref(frame);
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_free(&packet);
                    }
                }
            }
        }
        catch (Exception ex) when (!_stopping)
        {
            _broken = true;
            _failed(CredentialSanitizer.Sanitize(ex.Message));
        }
        catch (Exception)
        {
            // stopping
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.swr_free(&swr);
            ffmpeg.avcodec_free_context(&dec);
            DrainQueue();
        }
    }

    bool TryDequeue(out AVPacket* packet)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                packet = null;
                return false;
            }
            packet = (AVPacket*)_queue.Dequeue();
            return true;
        }
    }

    void DrainQueue()
    {
        while (TryDequeue(out var packet)) ffmpeg.av_packet_free(&packet);
    }

    public void Dispose()
    {
        _stopping = true;
        _signal.Release();
        _thread.Join(TimeSpan.FromSeconds(2));
        DrainQueue();
        var parameters = _parameters;
        ffmpeg.avcodec_parameters_free(&parameters);
        _parameters = null;
    }
}
```

Notes for the implementer (verify against FFmpeg.AutoGen 9.0.1.1; adjust names only if the binding differs):
- `swr_alloc_set_opts2` takes `SwrContext**`, `AVChannelLayout*` out, sample format, rate, `AVChannelLayout*` in, format, rate, log offset, log ctx.
- `frame->extended_data` is `byte**`; `swr_convert(SwrContext*, byte**, int, byte**, int)`.
- The "input identity" tuple only needs to change when rate/format/channel count change; simplify it to `(sample_rate, format, ch_layout.nb_channels)`.
- `CredentialSanitizer` is `internal`/`public` in Media — reuse it.
- If `Thread.Join` times out, do not free the decoder from the caller thread (the pump frees its own in `finally`); only free `_parameters` after a successful join, otherwise leak it (log nothing) — correctness over tidiness.

- [ ] **Step 4: Hook into StreamSession**

In `StreamSession.cs`:

```csharp
    volatile IAudioSink? _audioSink;
    AudioPump? _audioPump;           // current connection's pump (session thread)
    long _audioFramesDecodedBefore;  // frames of earlier connections

    /// <summary>Attaches (or with null, detaches) the audio output. Any thread; never reconnects.</summary>
    public void SetAudioSink(IAudioSink? sink) => _audioSink = sink;

    /// <summary>Raised on the audio thread when this connection's audio cannot be decoded; video continues.</summary>
    public event Action<string>? AudioFailed;

    internal long AudioFramesDecoded => _audioFramesDecodedBefore + (_audioPump?.FramesDecoded ?? 0);
```

In `PlayOnce`, after Task 7's `_audioIndex` code, when decoding and there is audio:

```csharp
            if (_decode && _audioIndex >= 0)
                _audioPump = new AudioPump(fmt->streams[_audioIndex]->codecpar, () => _audioSink, RaiseAudioFailed);
```

In the read loop add a branch after the video one:

```csharp
                else if (pkt->stream_index == _audioIndex && _audioPump is { } pump && _audioSink is not null)
                    pump.Enqueue(pkt);
```

In the `finally` (before closing `fmt`): `if (_audioPump is { } p) { _audioFramesDecodedBefore += p.FramesDecoded; p.Dispose(); _audioPump = null; }`.

```csharp
    void RaiseAudioFailed(string message)
    {
        try { AudioFailed?.Invoke(message); }
        catch (Exception) { /* isolated */ }
    }
```

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test tests/Centinela.Core.Tests` and `dotnet test tests/Centinela.Media.Tests` — Expected: all pass. Also run the Media suite twice to catch flakiness.

```bash
git add src/Centinela.Core/AudioCoordinator.cs tests/Centinela.Core.Tests/AudioCoordinatorTests.cs src/Centinela.Media/IAudioSink.cs src/Centinela.Media/AudioPump.cs src/Centinela.Media/StreamSession.cs tests/Centinela.Media.Tests/AudioTests.cs
git commit -m "feat(media): audio decode pump with 48 kHz stereo sink and single-source coordinator"
```

---

### Task 11: Audio output and the 🔇/🔊 button (App)

**Files:**
- Modify: `src/Centinela.App/Centinela.App.csproj` (NAudio)
- Create: `src/Centinela.App/AudioOutput.cs`, `src/Centinela.App/MainWindow.Audio.cs`
- Modify: `src/Centinela.App/CameraTile.xaml`, `src/Centinela.App/CameraTile.xaml.cs`, `src/Centinela.App/MainWindow.xaml.cs`, `src/Centinela.App/MainWindow.View.cs`, `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: `IAudioSink`, `StreamSession.SetAudioSink/AudioFailed/Info/InfoAvailable` (Tasks 7, 10); `AudioCoordinator<T>` (Task 10); `ToastStyle` (Task 6).
- Produces: `CameraTile.AudioCapable : bool`, `event Action<CameraTile>? AudioToggleRequested`, `event Action<CameraTile, string>? AudioFailed`, `void SetAudio(IAudioSink? sink)`, `bool AudioOn`.

- [ ] **Step 1: Package and output**

`Centinela.App.csproj`: `<ItemGroup><PackageReference Include="NAudio.Wasapi" Version="2.2.1" /></ItemGroup>`. Run `dotnet restore src/Centinela.App`.

`AudioOutput.cs`:

```csharp
using System.Runtime.InteropServices;
using Centinela.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Centinela.App;

/// <summary>WASAPI shared-mode output (50 ms). Keeps at most 150 ms queued: lagging audio is dropped, never delayed.</summary>
sealed class AudioOutput : IAudioSink, IDisposable
{
    static readonly TimeSpan MaxQueued = TimeSpan.FromMilliseconds(150);
    readonly WasapiOut _output;
    readonly BufferedWaveProvider _buffer;

    AudioOutput()
    {
        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        _output = new WasapiOut(AudioClientShareMode.Shared, 50);
        _output.Init(_buffer);
        _output.Play();
    }

    /// <summary>Null when Windows has no usable output device.</summary>
    public static AudioOutput? TryCreate()
    {
        try { return new AudioOutput(); }
        catch (Exception) { return null; }
    }

    public void Write(ReadOnlySpan<float> interleavedStereo)
    {
        if (_buffer.BufferedDuration > MaxQueued) _buffer.ClearBuffer();
        var bytes = MemoryMarshal.AsBytes(interleavedStereo);
        _buffer.AddSamples(bytes.ToArray(), 0, bytes.Length);
    }

    public void Clear() => _buffer.ClearBuffer();

    public void Dispose()
    {
        _output.Stop();
        _output.Dispose();
    }
}
```

If `WasapiOut.Init` rejects 48 kHz float on a device, NAudio 2.2.1 inserts a resampler in shared mode; if it still throws, `TryCreate` returns null and the UI shows "No hay salida de audio".

- [ ] **Step 2: Tile button**

`CameraTile.xaml`: add `<Button x:Name="AudioButton" Content="🔇" ToolTip="Activar sonido" Style="{StaticResource TileButton}" Click="Audio_Click" Visibility="Collapsed"/>` as the first button of `Actions`, and next to `NameLabel` a `TextBlock x:Name="AudioIndicator" Text=" 🔊" Foreground="White" Visibility="Collapsed"/>` (visible while sounding even without hover).

`CameraTile.xaml.cs`:

```csharp
    /// <summary>Set by the owner for big (Main) tiles and fullscreen; the button shows only if the stream has audio.</summary>
    public bool AudioCapable { get; set; }
    public bool AudioOn { get; private set; }
    public event Action<CameraTile>? AudioToggleRequested;
    public event Action<CameraTile, string>? AudioFailed;

    void Audio_Click(object sender, RoutedEventArgs e) => AudioToggleRequested?.Invoke(this);

    /// <summary>Plays this tile's audio into <paramref name="sink"/>, or silences it with null.</summary>
    public void SetAudio(IAudioSink? sink)
    {
        AudioOn = sink is not null && !_disposed;
        _session?.SetAudioSink(AudioOn ? sink : null);
        AudioButton.Content = AudioOn ? "🔊" : "🔇";
        AudioButton.ToolTip = AudioOn ? "Silenciar" : "Activar sonido";
        AudioIndicator.Visibility = AudioOn ? Visibility.Visible : Visibility.Collapsed;
    }

    public void DisableAudio(string reason)
    {
        AudioButton.IsEnabled = false;
        AudioButton.ToolTip = reason;
    }

    void UpdateAudioButton() =>
        AudioButton.Visibility = AudioCapable && _session?.Info?.AudioCodec is not null ? Visibility.Visible : Visibility.Collapsed;
```

In the constructor after creating `_session`:

```csharp
        _session.InfoAvailable += _ => Dispatcher.BeginInvoke(UpdateAudioButton);
        _session.AudioFailed += message => Dispatcher.BeginInvoke(() => { if (!_disposed) AudioFailed?.Invoke(this, message); });
```

`ShutdownAsync`: before stopping, `_session?.SetAudioSink(null); AudioOn = false;`.

- [ ] **Step 3: Window wiring**

`MainWindow.Audio.cs`:

```csharp
using Centinela.Core;

namespace Centinela.App;

/// <summary>Audio: at most one tile (big or fullscreen) plays, through one shared WASAPI output.</summary>
public partial class MainWindow
{
    readonly AudioCoordinator<CameraTile> _audio = new();
    AudioOutput? _audioOutput;
    bool _audioUnavailable;

    void WireAudio(CameraTile tile)
    {
        tile.AudioToggleRequested += ToggleAudio;
        tile.AudioFailed += (t, message) =>
        {
            ReleaseAudio(t);
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, t.Camera.Name, "Audio", $"No se pudo reproducir el audio de «{t.Camera.Name}»", message));
            ShowToast(new Toast($"No se pudo reproducir el audio de «{t.Camera.Name}»", "El vídeo sigue funcionando.", ToastStyle.Error));
        };
        if (_audioUnavailable) tile.DisableAudio("No hay salida de audio");
    }

    void ToggleAudio(CameraTile tile)
    {
        if (tile.AudioOn)
        {
            ReleaseAudio(tile);
            return;
        }
        _audioOutput ??= AudioOutput.TryCreate();
        if (_audioOutput is null)
        {
            _audioUnavailable = true;
            tile.DisableAudio("No hay salida de audio");
            Notify("No hay salida de audio en este equipo.", null);
            return;
        }
        _audio.Activate(tile)?.SetAudio(null);
        _audioOutput.Clear();
        tile.SetAudio(_audioOutput);
    }

    /// <summary>Silences the tile if it is the one playing (tile removed, fullscreen closed, failure).</summary>
    void ReleaseAudio(CameraTile tile)
    {
        tile.SetAudio(null);
        if (_audio.Deactivate(tile)) _audioOutput?.Clear();
    }
}
```

- `CreateTile`: `tile.AudioCapable = kind == StreamKind.Main; WireAudio(tile);`
- `ShowFullscreen`: `window.Tile.AudioCapable = true; WireAudio(window.Tile);` and in `window.Closed` call `ReleaseAudio(window.Tile)` before `TrackShutdown`.
- `RemoveAndShutdown(tile)`: call `ReleaseAudio(tile)` first (covers layout changes, placeholders, deletions and `HideToTray`).
- `OnClosed`: after the wait, `_audioOutput?.Dispose();`.

- [ ] **Step 4: Notices, build, smoke**

`THIRD-PARTY-NOTICES.md`: add a section "NAudio (NAudio.Wasapi, NAudio.Core) — MIT License — Copyright 2020 Mark Heath — https://github.com/naudio/NAudio" with the MIT text reference.

Build; smoke with `CENTINELA_DATA_DIR` and mediamtx publishing `av` (fixture command from Task 7): add camera `rtsp://127.0.0.1:18554/av`, switch to "Principal + miniaturas", 🔊 appears on the big tile only, sound plays within ~0.2 s, turning on another big tile's audio (Dual) silences the first, hiding to the tray stops sound, thumbnails show no button, a camera without audio (`open`) shows no button.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.App/Centinela.App.csproj src/Centinela.App/AudioOutput.cs src/Centinela.App/MainWindow.Audio.cs src/Centinela.App/CameraTile.xaml src/Centinela.App/CameraTile.xaml.cs src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.View.cs THIRD-PARTY-NOTICES.md
git commit -m "feat(app): single-camera audio with mute button via WASAPI"
```

---

### Task 12: Documentation

**Files:**
- Modify: `README.md`, `src/Centinela.App/InfoDocuments.cs`

- [ ] **Step 1: README**

Features list: add the two new layouts (with the click/drag rule of Dual), right-click menu (Duplicar, Probar with resolution/codec), digital zoom (wheel, drag, `0`), audio (only big/fullscreen, one at a time, 🔇 by default), Ayuda menu with "Buscar actualizaciones…" (daily, can be disabled, never downloads by itself) and "Acerca de". Add a "Publicar una versión" note: set `<Version>` in `Directory.Build.props`, create a GitHub release with tag `vX.Y.Z` on `tecxion/centinela`. Mention that the updates check sends only the app version to api.github.com.

- [ ] **Step 2: Manual**

`InfoDocuments.Manual()`: add short paragraphs (same voice as existing ones) for: vistas (derecha/izquierda/dos principales), clic derecho, zoom, sonido, actualizaciones. Keep it concise.

- [ ] **Step 3: Build, full tests, commit**

Run: `dotnet build Centinela.slnx -p:BaseOutputPath=<scratch>\bin\ -v q`, `dotnet test tests/Centinela.Core.Tests`, `dotnet test tests/Centinela.Media.Tests`. Expected: 0 warnings, all pass.

```bash
git add README.md src/Centinela.App/InfoDocuments.cs
git commit -m "docs: v1.2 features in README and in-app manual"
```
