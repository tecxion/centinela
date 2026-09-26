# CamaraWin v1.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the featured ("Principal + miniaturas") layout, classified and translated connection errors with toasts and an error log, a menu bar with encrypted JSON backup/restore and info windows, system-tray mode with single instance and start-with-Windows, "Grabar todas", and a per-tile stats overlay.

**Architecture:**
- Core gains pure, tested units: `ViewPlanner` (layout slots), `ErrorTranslator`, `ErrorNotificationPolicy`, `ErrorLog`, `CameraBackup`, `BackupMerge`, `BackupWriter` and `AutoStart`.
- Media gains error classification (FFmpeg log capture, RTSP status parsing, credential sanitizing), an `ErrorOccurred` event with a retry policy, and `StreamStats`.
- In the App, recordings move out of tiles into a window-level `RecordingController`, so that layout changes and tray mode never stop a recording. The UI is then layered on top of that.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF (+ WinForms `NotifyIcon` only), FFmpeg.AutoGen 9.0.1.1 / FFmpeg 9.0 LGPL, xUnit 2 + Xunit.SkippableFact, mediamtx for integration tests.

**Spec:** `docs/superpowers/specs/2026-09-26-featured-layout-and-errors-design.md` (base: `docs/superpowers/specs/2026-09-26-camara-win-design.md`)

## Global Constraints

- All projects target `net10.0-windows` via `Directory.Build.props`; solution `CamaraWin.slnx`.
- FFmpeg.AutoGen API lives in namespace `FFmpeg.AutoGen`. Verified names:
  - `ffmpeg.av_log_set_callback(av_log_set_callback_callback_func)`. The delegate is `av_log_set_callback_callback(void* p0, int level, string format, byte* vl)`, and there is an implicit conversion to `_func`.
  - `ffmpeg.av_log_format_line(void*, int, string, byte*, byte*, int, int*)`.
  - `ffmpeg.AV_LOG_WARNING` = 24 and `ffmpeg.AV_LOG_ERROR` = 16.
  - `ffmpeg.AVERROR_HTTP_UNAUTHORIZED`, `AVERROR_HTTP_FORBIDDEN`, `AVERROR_HTTP_NOT_FOUND`, `AVERROR_HTTP_SERVER_ERROR`, `AVERROR_EXIT`, `AVERROR_EOF`.
  - errno constants are NOT exposed. The build maps Winsock errors to UCRT errno values: ETIMEDOUT 138 (observed as -138), ECONNREFUSED 107, EHOSTUNREACH 110, ENETUNREACH 118, EADDRNOTAVAIL 101, ECONNRESET 108, EACCES 13.
- Credentials never reach disk, logs, toasts or UI text in plaintext. Every error `Detail` goes through `CredentialSanitizer.Sanitize`.
- UI text is Spanish; identifiers and comments are English.
- Session events fire on background threads. App code marshals with `Dispatcher.BeginInvoke` and never uses `Dispatcher.Invoke` from session or recorder threads.
- Nothing may block the UI thread on a session join. Shutdowns run via `Task.Run` and are tracked; app exit does one bounded wait of 8 s.
- `git add` explicit paths only. Never commit `ffmpeg/`, `tools/bin/`, `bin/`, `obj/`, or mediamtx `auto.crt`/`auto.key`. Every commit message ends with a blank line and then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Support links are exactly `https://www.tecxart.es` and `mailto:tecxart@gmail.com`.
- Backup crypto is exactly: PBKDF2-SHA256, 600000 iterations, 16-byte salt, 32-byte key; AES-256-GCM with a 12-byte nonce and a 16-byte tag; associated data `name|host|port`.

## File Map

```
src/CamaraWin.Core/
  Settings.cs                 + LayoutMode, LayoutMode/FeaturedCameraId/ShowStats/BackupFolder/TrayHintShown
  GridLayout.cs               + FeaturedLayout, ComputeFeatured
  ViewPlanner.cs              TileSlot, ViewPlan, ViewPlanner.Plan          (new)
  AppPaths.cs                 + LogsDirectory, DefaultBackupDirectory
  Errors/StreamErrorKind.cs   StreamErrorKind, StreamError                  (new)
  Errors/ErrorTranslator.cs   TranslatedError, ErrorTranslator              (new)
  Errors/ErrorNotificationPolicy.cs                                         (new)
  Errors/ErrorLog.cs          ErrorLogEntry, ErrorLog                       (new)
  Backup/CameraBackup.cs      export/import, exceptions                     (new)
  Backup/BackupMerge.cs       MergeResult, BackupMerge                      (new)
  Backup/BackupWriter.cs      automatic copy                                (new)
  AutoStart.cs                IRunKey, RegistryRunKey, AutoStart            (new)
src/CamaraWin.Media/
  CamaraWin.Media.csproj      + ProjectReference Core
  FFmpegLog.cs                av_log capture routed per AVFormatContext     (new)
  RtspStatusParser.cs         (new)
  CredentialSanitizer.cs      (new)
  StreamErrorClassifier.cs    (new)
  StreamStats.cs              (new)
  StreamSession.cs            ErrorOccurred, LastErrorKind, retry policy, Stats
  FFmpegLoader.cs             installs FFmpegLog
src/CamaraWin.App/
  RecordingController.cs      per-camera headless recordings              (new)
  MainWindow.View.cs          view building (grid/featured), tile map      (new, partial)
  MainWindow.Errors.cs        ErrorCenter wiring, toasts, log window       (new, partial)
  MainWindow.Menu.cs          menu handlers, import/export, auto backup    (new, partial)
  MainWindow.Tray.cs          tray, hide/show, exit                        (new, partial)
  ErrorCenter.cs, Toast.cs    (new)
  ToastHost.xaml(.cs), ErrorLogWindow.xaml(.cs)                            (new)
  PassphraseDialog.xaml(.cs), ExportDialog.xaml(.cs)                       (new)
  InfoWindow.xaml(.cs), InfoDocuments.cs                                   (new)
  TrayController.cs, TrayIcons.cs, SingleInstance.cs                       (new)
  CameraTile.xaml(.cs), MainWindow.xaml(.cs), FullscreenWindow.xaml.cs, App.xaml.cs, CamaraWin.App.csproj  (modified)
docs/ejemplo-camaras.json      (new)
README.md                      (updated)
```

---

### Task 1: Core — layout settings, featured geometry and view planner

**Files:**
- Modify: `src/CamaraWin.Core/Settings.cs`, `src/CamaraWin.Core/GridLayout.cs`
- Create: `src/CamaraWin.Core/ViewPlanner.cs`
- Test: `tests/CamaraWin.Core.Tests/GridLayoutTests.cs` (append), `tests/CamaraWin.Core.Tests/ViewPlannerTests.cs`, `tests/CamaraWin.Core.Tests/SettingsStoreTests.cs` (append)

**Interfaces:**
- Produces:
  - `enum LayoutMode { Grid, Featured }`.
  - New `AppSettings` properties: `LayoutMode LayoutMode` (default Grid), `Guid? FeaturedCameraId`, `bool ShowStats`, `string? BackupFolder`, `bool TrayHintShown`.
  - `sealed record FeaturedLayout(int Size, int FeaturedSpan, IReadOnlyList<(int Row, int Column)> Slots)` and `static FeaturedLayout GridLayout.ComputeFeatured(int cameraCount)`.
  - `readonly record struct TileSlot(Guid CameraId, StreamKind Kind, int Row, int Column, int RowSpan, int ColumnSpan)`.
  - `sealed record ViewPlan(int Rows, int Columns, IReadOnlyList<TileSlot> Slots)`.
  - `static ViewPlan ViewPlanner.Plan(IReadOnlyList<Camera> cameras, LayoutMode mode, GridMode gridMode, Guid? featuredCameraId)`.

- [ ] **Step 1: Failing tests.** Append to `GridLayoutTests.cs`:

```csharp
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(5, 3, 2)]
    [InlineData(7, 4, 3)]
    [InlineData(8, 4, 3)]
    [InlineData(9, 5, 4)]
    [InlineData(10, 5, 4)]
    public void Featured_size_and_span(int count, int size, int span)
    {
        var layout = GridLayout.ComputeFeatured(count);
        Assert.Equal((size, span), (layout.Size, layout.FeaturedSpan));
        Assert.Equal(Math.Max(0, count - 1), layout.Slots.Count);
    }

    [Fact]
    public void Featured_slots_for_seven_go_right_column_then_bottom_row() =>
        Assert.Equal(new[] { (0, 3), (1, 3), (2, 3), (3, 0), (3, 1), (3, 2) },
            GridLayout.ComputeFeatured(7).Slots.ToArray());

    [Fact]
    public void Featured_slots_for_eight_fill_the_corner() =>
        Assert.Equal((3, 3), GridLayout.ComputeFeatured(8).Slots[^1]);
```

Create `tests/CamaraWin.Core.Tests/ViewPlannerTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class ViewPlannerTests
{
    static List<Camera> Cameras(int n) =>
        Enumerable.Range(0, n).Select(i => new Camera { Name = $"C{i}", Order = n - 1 - i }).ToList();

    [Fact]
    public void Empty_list_has_no_slots() =>
        Assert.Empty(ViewPlanner.Plan([], LayoutMode.Featured, GridMode.Auto, null).Slots);

    [Fact]
    public void Grid_mode_places_substreams_row_major_by_order()
    {
        var cams = Cameras(7);
        var plan = ViewPlanner.Plan(cams, LayoutMode.Grid, GridMode.Auto, null);
        Assert.Equal((3, 3), (plan.Rows, plan.Columns));
        var byOrder = cams.OrderBy(c => c.Order).ToList();
        for (var i = 0; i < 7; i++)
            Assert.Equal(new TileSlot(byOrder[i].Id, StreamKind.Sub, i / 3, i % 3, 1, 1), plan.Slots[i]);
    }

    [Fact]
    public void Grid_fixed_mode_hides_extra_cameras() =>
        Assert.Equal(4, ViewPlanner.Plan(Cameras(7), LayoutMode.Grid, GridMode.Four, null).Slots.Count);

    [Fact]
    public void Featured_defaults_to_first_by_order_on_main_stream()
    {
        var cams = Cameras(7);
        var first = cams.OrderBy(c => c.Order).First();
        var plan = ViewPlanner.Plan(cams, LayoutMode.Featured, GridMode.Auto, null);
        Assert.Equal((4, 4), (plan.Rows, plan.Columns));
        Assert.Equal(new TileSlot(first.Id, StreamKind.Main, 0, 0, 3, 3), plan.Slots[0]);
        Assert.Equal(7, plan.Slots.Count);
        Assert.All(plan.Slots.Skip(1), s => Assert.Equal((StreamKind.Sub, 1, 1), (s.Kind, s.RowSpan, s.ColumnSpan)));
    }

    [Fact]
    public void Featured_uses_selected_camera_and_keeps_others_in_order()
    {
        var cams = Cameras(4);
        var byOrder = cams.OrderBy(c => c.Order).ToList();
        var plan = ViewPlanner.Plan(cams, LayoutMode.Featured, GridMode.Auto, byOrder[2].Id);
        Assert.Equal(byOrder[2].Id, plan.Slots[0].CameraId);
        Assert.Equal(new[] { byOrder[0].Id, byOrder[1].Id, byOrder[3].Id }, plan.Slots.Skip(1).Select(s => s.CameraId).ToArray());
    }

    [Fact]
    public void Featured_unknown_id_falls_back_to_first()
    {
        var cams = Cameras(3);
        Assert.Equal(cams.OrderBy(x => x.Order).First().Id,
            ViewPlanner.Plan(cams, LayoutMode.Featured, GridMode.Auto, Guid.NewGuid()).Slots[0].CameraId);
    }

    [Fact]
    public void Featured_single_camera_fills_window_with_main()
    {
        var cam = Cameras(1);
        Assert.Equal(new TileSlot(cam[0].Id, StreamKind.Main, 0, 0, 1, 1),
            Assert.Single(ViewPlanner.Plan(cam, LayoutMode.Featured, GridMode.Auto, null).Slots));
    }
}
```

Append to `SettingsStoreTests.cs`:

```csharp
    [Fact]
    public void New_settings_round_trip()
    {
        var id = Guid.NewGuid();
        new SettingsStore(FilePath).Save(new AppSettings
        {
            LayoutMode = LayoutMode.Featured, FeaturedCameraId = id, ShowStats = true,
            BackupFolder = @"D:\Copias", TrayHintShown = true,
        });
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal((LayoutMode.Featured, id, true, @"D:\Copias", true),
            (s.LayoutMode, s.FeaturedCameraId!.Value, s.ShowStats, s.BackupFolder, s.TrayHintShown));
    }

    [Fact]
    public void Undefined_layout_mode_becomes_grid()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "layoutMode": 7 }""");
        Assert.Equal(LayoutMode.Grid, new SettingsStore(FilePath).Load().LayoutMode);
    }
```

- [ ] **Step 2: Run to verify failure.** `dotnet test tests/CamaraWin.Core.Tests` → build fails (`ComputeFeatured`, `ViewPlanner`, `LayoutMode` missing).

- [ ] **Step 3: Implement.** In `Settings.cs`, add after the `GridMode` enum:

```csharp
public enum LayoutMode { Grid, Featured }
```

Add these properties to `AppSettings`:

```csharp
    public LayoutMode LayoutMode { get; set; } = LayoutMode.Grid;
    public Guid? FeaturedCameraId { get; set; }
    public bool ShowStats { get; set; }
    public string? BackupFolder { get; set; }
    public bool TrayHintShown { get; set; }
```

In `SettingsStore.Load`, after the GridMode normalization, add:

```csharp
        if (!Enum.IsDefined(settings.LayoutMode)) settings.LayoutMode = LayoutMode.Grid;
```

Add to `GridLayout.cs` (the record goes at namespace level):

```csharp
public sealed record FeaturedLayout(int Size, int FeaturedSpan, IReadOnlyList<(int Row, int Column)> Slots);
```

```csharp
    /// <summary>
    /// Featured camera spans k×k cells of a (k+1)×(k+1) grid; the others fill the right column
    /// (top→bottom) then the bottom row (left→right). k = max(1, ceil((n−2)/2)).
    /// </summary>
    public static FeaturedLayout ComputeFeatured(int cameraCount)
    {
        if (cameraCount <= 1) return new FeaturedLayout(1, 1, []);
        var k = Math.Max(1, (int)Math.Ceiling((cameraCount - 2) / 2.0));
        var slots = new List<(int Row, int Column)>();
        for (var row = 0; row < k; row++) slots.Add((row, k));
        for (var column = 0; column <= k; column++) slots.Add((k, column));
        return new FeaturedLayout(k + 1, k, slots.Take(cameraCount - 1).ToList());
    }
```

Create `src/CamaraWin.Core/ViewPlanner.cs`:

```csharp
namespace CamaraWin.Core;

public readonly record struct TileSlot(Guid CameraId, StreamKind Kind, int Row, int Column, int RowSpan, int ColumnSpan);

public sealed record ViewPlan(int Rows, int Columns, IReadOnlyList<TileSlot> Slots);

public static class ViewPlanner
{
    public static ViewPlan Plan(IReadOnlyList<Camera> cameras, LayoutMode mode, GridMode gridMode, Guid? featuredCameraId)
    {
        var ordered = cameras.OrderBy(c => c.Order).ToList();
        if (ordered.Count == 0) return new ViewPlan(1, 1, []);

        if (mode == LayoutMode.Featured)
        {
            var featured = ordered.FirstOrDefault(c => c.Id == featuredCameraId) ?? ordered[0];
            var layout = GridLayout.ComputeFeatured(ordered.Count);
            var slots = new List<TileSlot>
            {
                new(featured.Id, StreamKind.Main, 0, 0, layout.FeaturedSpan, layout.FeaturedSpan),
            };
            slots.AddRange(ordered.Where(c => c.Id != featured.Id).Select((c, i) =>
                new TileSlot(c.Id, StreamKind.Sub, layout.Slots[i].Row, layout.Slots[i].Column, 1, 1)));
            return new ViewPlan(layout.Size, layout.Size, slots);
        }

        var visible = ordered.Take(GridLayout.VisibleCount(ordered.Count, gridMode)).ToList();
        var size = GridLayout.Compute(visible.Count, gridMode);
        return new ViewPlan(size.Rows, size.Columns, visible
            .Select((c, i) => new TileSlot(c.Id, StreamKind.Sub, i / size.Columns, i % size.Columns, 1, 1))
            .ToList());
    }
}
```

- [ ] **Step 4: Run to verify pass.** `dotnet test tests/CamaraWin.Core.Tests` → all pass.
- [ ] **Step 5: Commit.** `git add src/CamaraWin.Core tests/CamaraWin.Core.Tests && git commit -m "feat(core): featured layout geometry and view planner"` (with the trailer).

---

### Task 2: Core — error kinds, Spanish translation, notification policy, error log

**Files:**
- Create: `src/CamaraWin.Core/Errors/StreamErrorKind.cs`, `Errors/ErrorTranslator.cs`, `Errors/ErrorNotificationPolicy.cs`, `Errors/ErrorLog.cs`
- Modify: `src/CamaraWin.Core/AppPaths.cs`
- Test: `tests/CamaraWin.Core.Tests/ErrorTranslatorTests.cs`, `ErrorNotificationPolicyTests.cs`, `ErrorLogTests.cs`

**Interfaces:**
- Produces (namespace `CamaraWin.Core`):
  - `enum StreamErrorKind { AuthFailed, Unreachable, NotFound, CameraBusy, ServerError, Stalled, Unknown }`.
  - `sealed record StreamError(StreamErrorKind Kind, int? RtspStatus, int FfmpegCode, string Detail, DateTime TimeUtc)`.
  - `sealed record TranslatedError(string Short, string Title, string Advice)` and `static TranslatedError ErrorTranslator.Translate(StreamErrorKind, Brand, string cameraName)`.
  - `sealed class ErrorNotificationPolicy` with `bool ShouldNotify(Guid cameraId, StreamErrorKind kind)`, `bool OnPlaying(Guid cameraId)` and `void Forget(Guid cameraId)`.
  - `sealed record ErrorLogEntry(DateTime Time, string Camera, string Kind, string Title, string Detail)`.
  - `sealed class ErrorLog(string directory) : IDisposable` with `const int Capacity = 500`, `event Action<ErrorLogEntry>? EntryAdded`, `void Add(ErrorLogEntry)`, `IReadOnlyList<ErrorLogEntry> Snapshot()` (newest first), `void Clear()`, and statics `string FileFor(string dir, DateTime time)`, `string FormatLine(ErrorLogEntry)`, `int PurgeOlderThan(string dir, DateTime now, int days = 14)`.
  - `AppPaths.LogsDirectory` (`DataDirectory\logs`) and `AppPaths.DefaultBackupDirectory` (`MyDocuments\CamaraWin`).

- [ ] **Step 1: Failing tests.**

`ErrorTranslatorTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class ErrorTranslatorTests
{
    public static TheoryData<StreamErrorKind, Brand> All()
    {
        var data = new TheoryData<StreamErrorKind, Brand>();
        foreach (var k in Enum.GetValues<StreamErrorKind>())
            foreach (var b in Enum.GetValues<Brand>()) data.Add(k, b);
        return data;
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_kind_and_brand_has_spanish_text(StreamErrorKind kind, Brand brand)
    {
        var t = ErrorTranslator.Translate(kind, brand, "Garaje");
        Assert.False(string.IsNullOrWhiteSpace(t.Short));
        Assert.True(t.Short.Length <= 30, t.Short);
        Assert.StartsWith("Garaje", t.Title);
        Assert.False(string.IsNullOrWhiteSpace(t.Advice));
    }

    [Fact]
    public void Tapo_auth_advice_mentions_camera_account() =>
        Assert.Contains("Cuenta de cámara", ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Tapo, "X").Advice);

    [Fact]
    public void Imou_auth_advice_mentions_safety_code() =>
        Assert.Contains("código de seguridad", ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Imou, "X").Advice);

    [Fact]
    public void Imou_not_found_advice_mentions_rtsp() =>
        Assert.Contains("RTSP", ErrorTranslator.Translate(StreamErrorKind.NotFound, Brand.Imou, "X").Advice);

    [Fact]
    public void Short_texts_match_spec()
    {
        Assert.Equal("Contraseña incorrecta", ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Custom, "X").Short);
        Assert.Equal("Sin conexión", ErrorTranslator.Translate(StreamErrorKind.Unreachable, Brand.Custom, "X").Short);
        Assert.Equal("Vídeo no disponible", ErrorTranslator.Translate(StreamErrorKind.NotFound, Brand.Custom, "X").Short);
        Assert.Equal("Cámara ocupada", ErrorTranslator.Translate(StreamErrorKind.CameraBusy, Brand.Custom, "X").Short);
        Assert.Equal("Error de la cámara", ErrorTranslator.Translate(StreamErrorKind.ServerError, Brand.Custom, "X").Short);
        Assert.Equal("Imagen congelada", ErrorTranslator.Translate(StreamErrorKind.Stalled, Brand.Custom, "X").Short);
        Assert.Equal("Error de conexión", ErrorTranslator.Translate(StreamErrorKind.Unknown, Brand.Custom, "X").Short);
    }
}
```

`ErrorNotificationPolicyTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class ErrorNotificationPolicyTests
{
    readonly Guid _cam = Guid.NewGuid();

    [Theory]
    [InlineData(StreamErrorKind.AuthFailed)]
    [InlineData(StreamErrorKind.NotFound)]
    [InlineData(StreamErrorKind.CameraBusy)]
    public void Immediate_kinds_notify_once(StreamErrorKind kind)
    {
        var p = new ErrorNotificationPolicy();
        Assert.True(p.ShouldNotify(_cam, kind));
        Assert.False(p.ShouldNotify(_cam, kind));
    }

    [Theory]
    [InlineData(StreamErrorKind.Unreachable)]
    [InlineData(StreamErrorKind.Stalled)]
    [InlineData(StreamErrorKind.ServerError)]
    [InlineData(StreamErrorKind.Unknown)]
    public void Transient_kinds_notify_on_third_consecutive_failure(StreamErrorKind kind)
    {
        var p = new ErrorNotificationPolicy();
        Assert.False(p.ShouldNotify(_cam, kind));
        Assert.False(p.ShouldNotify(_cam, kind));
        Assert.True(p.ShouldNotify(_cam, kind));
        Assert.False(p.ShouldNotify(_cam, kind));
    }

    [Fact]
    public void Playing_resets_and_reports_recovery_only_after_a_notice()
    {
        var p = new ErrorNotificationPolicy();
        p.ShouldNotify(_cam, StreamErrorKind.Unreachable);
        Assert.False(p.OnPlaying(_cam)); // no notice was shown
        p.ShouldNotify(_cam, StreamErrorKind.AuthFailed);
        Assert.True(p.OnPlaying(_cam));
        Assert.True(p.ShouldNotify(_cam, StreamErrorKind.AuthFailed)); // notifies again after recovery
    }

    [Fact]
    public void Cameras_are_independent()
    {
        var p = new ErrorNotificationPolicy();
        Assert.True(p.ShouldNotify(_cam, StreamErrorKind.AuthFailed));
        Assert.True(p.ShouldNotify(Guid.NewGuid(), StreamErrorKind.AuthFailed));
    }
}
```

`ErrorLogTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public sealed class ErrorLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-log-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static ErrorLogEntry Entry(int i, DateTime t) => new(t, $"Cam{i}", "Sin conexión", $"Cam{i}: no se puede conectar", $"detail {i}");

    [Fact]
    public void Keeps_newest_first_and_caps_capacity()
    {
        using var log = new ErrorLog(_dir);
        var t = new DateTime(2026, 9, 26, 10, 0, 0);
        for (var i = 0; i < ErrorLog.Capacity + 1; i++) log.Add(Entry(i, t));
        var snap = log.Snapshot();
        Assert.Equal(ErrorLog.Capacity, snap.Count);
        Assert.Equal("Cam500", snap[0].Camera);
    }

    [Fact]
    public void Writes_daily_file_after_dispose()
    {
        var t = new DateTime(2026, 9, 26, 10, 0, 0);
        using (var log = new ErrorLog(_dir)) log.Add(Entry(1, t));
        var text = File.ReadAllText(ErrorLog.FileFor(_dir, t));
        Assert.Contains("Cam1\tSin conexión\tCam1: no se puede conectar\tdetail 1", text);
    }

    [Fact]
    public void FormatLine_strips_tabs_and_newlines() =>
        Assert.Equal("2026-09-26 10:00:00\ta b\tk\tt\td e",
            ErrorLog.FormatLine(new ErrorLogEntry(new DateTime(2026, 9, 26, 10, 0, 0), "a\tb", "k", "t", "d\ne")));

    [Fact]
    public void Purge_deletes_only_old_log_files()
    {
        Directory.CreateDirectory(_dir);
        var now = new DateTime(2026, 9, 26);
        File.WriteAllText(ErrorLog.FileFor(_dir, now.AddDays(-20)), "old");
        File.WriteAllText(ErrorLog.FileFor(_dir, now.AddDays(-3)), "recent");
        File.WriteAllText(Path.Combine(_dir, "other.txt"), "keep");
        Assert.Equal(1, ErrorLog.PurgeOlderThan(_dir, now));
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void EntryAdded_fires_and_Clear_empties_memory_only()
    {
        using var log = new ErrorLog(_dir);
        ErrorLogEntry? seen = null;
        log.EntryAdded += e => seen = e;
        log.Add(Entry(1, DateTime.Now));
        Assert.NotNull(seen);
        log.Clear();
        Assert.Empty(log.Snapshot());
    }
}
```

- [ ] **Step 2: Run to verify failure.** `dotnet test tests/CamaraWin.Core.Tests` → build fails.

- [ ] **Step 3: Implement.** Add to `AppPaths.cs`:

```csharp
    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string DefaultBackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppFolder);
```

`Errors/StreamErrorKind.cs`:

```csharp
namespace CamaraWin.Core;

public enum StreamErrorKind { AuthFailed, Unreachable, NotFound, CameraBusy, ServerError, Stalled, Unknown }

/// <summary>One failed connection attempt or stall. Detail is sanitized (never contains credentials).</summary>
public sealed record StreamError(StreamErrorKind Kind, int? RtspStatus, int FfmpegCode, string Detail, DateTime TimeUtc);
```

`Errors/ErrorTranslator.cs`:

```csharp
namespace CamaraWin.Core;

public sealed record TranslatedError(string Short, string Title, string Advice);

public static class ErrorTranslator
{
    public static TranslatedError Translate(StreamErrorKind kind, Brand brand, string cameraName) => kind switch
    {
        StreamErrorKind.AuthFailed => new("Contraseña incorrecta", $"{cameraName}: usuario o contraseña incorrectos", brand switch
        {
            Brand.Tapo => "Usa la «Cuenta de cámara» que creaste en la app Tapo (Ajustes avanzados), no tu cuenta de TP-Link.",
            Brand.Imou => "El usuario es «admin» y la contraseña es el código de seguridad de la pegatina de la cámara.",
            _ => "Revisa el usuario y la contraseña de la cámara.",
        }),
        StreamErrorKind.Unreachable => new("Sin conexión", $"{cameraName}: no se puede conectar con la cámara",
            "Comprueba que la cámara está encendida y en la misma red, y que su IP no ha cambiado (conviene reservarla en el router)."),
        StreamErrorKind.NotFound => new("Vídeo no disponible", $"{cameraName}: la cámara no ofrece el vídeo pedido", brand switch
        {
            Brand.Imou => "Activa RTSP/ONVIF en la app Imou (ajustes de la cámara) y vuelve a intentarlo.",
            Brand.Tapo => "Crea la «Cuenta de cámara» en la app Tapo (Ajustes avanzados); sin ella no hay vídeo RTSP.",
            _ => "Revisa la URL RTSP en «Editar › Avanzado».",
        }),
        StreamErrorKind.CameraBusy => new("Cámara ocupada", $"{cameraName}: la cámara no acepta más conexiones",
            "Hay demasiadas conexiones a la vez: cierra la app oficial, la pantalla completa o una grabación de esta cámara."),
        StreamErrorKind.ServerError => new("Error de la cámara", $"{cameraName}: la cámara devolvió un error interno",
            "Reinicia la cámara. Si continúa, actualiza su firmware desde la app oficial."),
        StreamErrorKind.Stalled => new("Imagen congelada", $"{cameraName}: la cámara dejó de enviar vídeo",
            "Se reconectará sola. Si se repite, revisa la cobertura Wi-Fi de la cámara."),
        _ => new("Error de conexión", $"{cameraName}: error de conexión",
            "Consulta el detalle técnico en el registro (Registro, en la barra inferior)."),
    };
}
```

`Errors/ErrorNotificationPolicy.cs`:

```csharp
namespace CamaraWin.Core;

/// <summary>
/// Decides when a connection error deserves a toast: once per (camera, kind) until the camera plays
/// again; transient kinds only after <see cref="RepeatThreshold"/> consecutive failures. Not thread-safe.
/// </summary>
public sealed class ErrorNotificationPolicy
{
    public const int RepeatThreshold = 3;

    readonly Dictionary<Guid, CameraState> _states = [];

    sealed class CameraState
    {
        public int Failures;
        public readonly HashSet<StreamErrorKind> Notified = [];
    }

    public bool ShouldNotify(Guid cameraId, StreamErrorKind kind)
    {
        if (!_states.TryGetValue(cameraId, out var state)) _states[cameraId] = state = new CameraState();
        state.Failures++;
        if (state.Notified.Contains(kind)) return false;
        var immediate = kind is StreamErrorKind.AuthFailed or StreamErrorKind.NotFound or StreamErrorKind.CameraBusy;
        if (!immediate && state.Failures < RepeatThreshold) return false;
        state.Notified.Add(kind);
        return true;
    }

    /// <summary>Resets the camera. True when a notice had been shown (so a "recovered" notice makes sense).</summary>
    public bool OnPlaying(Guid cameraId) => _states.Remove(cameraId, out var state) && state.Notified.Count > 0;

    public void Forget(Guid cameraId) => _states.Remove(cameraId);
}
```

`Errors/ErrorLog.cs`:

```csharp
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace CamaraWin.Core;

public sealed record ErrorLogEntry(DateTime Time, string Camera, string Kind, string Title, string Detail);

/// <summary>In-memory ring of the latest entries plus one append-only file per day, written off the caller's thread.</summary>
public sealed class ErrorLog : IDisposable
{
    public const int Capacity = 500;
    const string Prefix = "camarawin-";

    readonly string _directory;
    readonly object _gate = new();
    readonly LinkedList<ErrorLogEntry> _entries = new();
    readonly BlockingCollection<ErrorLogEntry> _pending = new();
    readonly Task _writer;

    public ErrorLog(string directory)
    {
        _directory = directory;
        _writer = Task.Run(WriteLoop);
    }

    public event Action<ErrorLogEntry>? EntryAdded;

    public void Add(ErrorLogEntry entry)
    {
        lock (_gate)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity) _entries.RemoveLast();
        }
        if (!_pending.IsAddingCompleted) _pending.TryAdd(entry);
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<ErrorLogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    public static string FileFor(string directory, DateTime time) =>
        Path.Combine(directory, $"{Prefix}{time:yyyy-MM-dd}.log");

    public static string FormatLine(ErrorLogEntry e) => string.Join('\t',
        e.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        Clean(e.Camera), Clean(e.Kind), Clean(e.Title), Clean(e.Detail));

    public static int PurgeOlderThan(string directory, DateTime now, int days = 14)
    {
        if (!Directory.Exists(directory)) return 0;
        var deleted = 0;
        foreach (var file in Directory.GetFiles(directory, $"{Prefix}*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < now.Date.AddDays(-days))
            {
                try { File.Delete(file); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return deleted;
    }

    public void Dispose()
    {
        _pending.CompleteAdding();
        _writer.Wait(TimeSpan.FromSeconds(2));
    }

    void WriteLoop()
    {
        foreach (var entry in _pending.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(FileFor(_directory, entry.Time), FormatLine(entry) + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
```

- [ ] **Step 4: Run to verify pass.** `dotnet test tests/CamaraWin.Core.Tests` → all pass.
- [ ] **Step 5: Commit** `feat(core): error kinds, Spanish translations, notification policy and error log` (with the trailer).

---

### Task 3: Media — FFmpeg log capture, error classification, ErrorOccurred and retry policy

**Files:**
- Modify: `src/CamaraWin.Media/CamaraWin.Media.csproj` (add `<ProjectReference Include="..\CamaraWin.Core\CamaraWin.Core.csproj" />`), `src/CamaraWin.Media/FFmpegLoader.cs`, `src/CamaraWin.Media/StreamSession.cs`, `tests/CamaraWin.Media.Tests/Rtsp/RtspTestServer.cs` (anonymous read permission for path `missing`)
- Create: `src/CamaraWin.Media/FFmpegLog.cs`, `RtspStatusParser.cs`, `CredentialSanitizer.cs`, `StreamErrorClassifier.cs`
- Test: `tests/CamaraWin.Media.Tests/ErrorClassificationTests.cs`, `tests/CamaraWin.Media.Tests/StreamErrorTests.cs`

**Interfaces:**
- Consumes: `StreamErrorKind`, `StreamError` (Core, Task 2).
- Produces:
  - `static class FFmpegLog { Install(); Register(void* context, Action<string> sink); Unregister(void* context); }`.
  - `static int? RtspStatusParser.Parse(string line)`.
  - `static string CredentialSanitizer.Sanitize(string text)`.
  - `static StreamErrorKind StreamErrorClassifier.Classify(int ffmpegCode, int? rtspStatus, bool wasPlaying, bool deadlineHit)`.
  - New members on `StreamSession`: `event Action<StreamError>? ErrorOccurred`, `StreamErrorKind? LastErrorKind`. `LastError` now holds the sanitized Detail.
  - Behaviour: AuthFailed stops without retrying; NotFound and CameraBusy retry every 30 s; everything else uses backoff 1, 2, 4, 8, 10 s.

- [ ] **Step 1: Unit tests** — `ErrorClassificationTests.cs`:

```csharp
using CamaraWin.Core;
using CamaraWin.Media;
using FFmpeg.AutoGen;

namespace CamaraWin.Media.Tests;

public class ErrorClassificationTests
{
    public ErrorClassificationTests() => FFmpegLoader.Initialize();

    [Theory]
    [InlineData("[rtsp @ 000001] method DESCRIBE failed: 401 Unauthorized", 401)]
    [InlineData("method SETUP failed: 461 Unsupported transport", 461)]
    [InlineData("method DESCRIBE failed: 404 Not Found", 404)]
    [InlineData("RTSP/1.0 503 Service Unavailable", 503)]
    [InlineData("Connection to tcp://127.0.0.1:1 failed: Error number -138 occurred", null)]
    [InlineData("method OPTIONS failed: 200 OK", null)]
    public void Parses_rtsp_status(string line, int? expected) => Assert.Equal(expected, RtspStatusParser.Parse(line));

    [Theory]
    [InlineData("open rtsp://viewer:p%40ss@127.0.0.1:18554/secure failed", "open rtsp://127.0.0.1:18554/secure failed")]
    [InlineData("a rtsp://u:p@h/x and http://x:y@z/w", "a rtsp://h/x and http://z/w")]
    [InlineData("no credentials rtsp://h/x", "no credentials rtsp://h/x")]
    public void Sanitizer_removes_userinfo(string input, string expected) =>
        Assert.Equal(expected, CredentialSanitizer.Sanitize(input));

    [Theory]
    [InlineData(401, StreamErrorKind.AuthFailed)]
    [InlineData(403, StreamErrorKind.AuthFailed)]
    [InlineData(404, StreamErrorKind.NotFound)]
    [InlineData(454, StreamErrorKind.NotFound)]
    [InlineData(453, StreamErrorKind.CameraBusy)]
    [InlineData(503, StreamErrorKind.CameraBusy)]
    [InlineData(500, StreamErrorKind.ServerError)]
    [InlineData(461, StreamErrorKind.Unknown)]
    public void Status_wins(int status, StreamErrorKind kind) =>
        Assert.Equal(kind, StreamErrorClassifier.Classify(-1, status, wasPlaying: false, deadlineHit: false));

    [Fact]
    public void Ffmpeg_codes_classify()
    {
        Assert.Equal(StreamErrorKind.AuthFailed, StreamErrorClassifier.Classify(ffmpeg.AVERROR_HTTP_UNAUTHORIZED, null, false, false));
        Assert.Equal(StreamErrorKind.AuthFailed, StreamErrorClassifier.Classify(-13, null, false, false));
        Assert.Equal(StreamErrorKind.NotFound, StreamErrorClassifier.Classify(ffmpeg.AVERROR_HTTP_NOT_FOUND, null, false, false));
        Assert.Equal(StreamErrorKind.ServerError, StreamErrorClassifier.Classify(ffmpeg.AVERROR_HTTP_SERVER_ERROR, null, false, false));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(-138, null, false, false));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(-107, null, false, false));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(ffmpeg.AVERROR_EXIT, null, false, true));
        Assert.Equal(StreamErrorKind.Stalled, StreamErrorClassifier.Classify(ffmpeg.AVERROR_EXIT, null, true, true));
        Assert.Equal(StreamErrorKind.Unreachable, StreamErrorClassifier.Classify(ffmpeg.AVERROR_EOF, null, true, false));
        Assert.Equal(StreamErrorKind.Unknown, StreamErrorClassifier.Classify(-22, null, false, false));
    }
}
```

- [ ] **Step 2: Integration tests.** In `RtspTestServer.cs` config, add under the `any` user's permissions:

```yaml
              - action: read
                path: missing
```

(Nobody publishes to `missing`, so an anonymous read gets 404.) Then create `StreamErrorTests.cs`:

```csharp
using CamaraWin.Core;
using CamaraWin.Media.Tests.Rtsp;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class StreamErrorTests(RtspTestServer server)
{
    const string WrongPassword = "Wr0ngPass!";

    StreamSession Open(string url)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(url);
    }

    static StreamError? FirstError(StreamSession session, TimeSpan timeout)
    {
        StreamError? first = null;
        session.ErrorOccurred += e => first ??= e;
        session.Start();
        TestUtil.WaitFor(() => first is not null, timeout);
        return first;
    }

    [SkippableFact]
    public void Wrong_password_is_AuthFailed_with_status_401_and_no_credentials()
    {
        using var session = Open(server.Url("secure", RtspTestServer.SecureUser, WrongPassword));
        var error = FirstError(session, TimeSpan.FromSeconds(10));
        Assert.NotNull(error);
        Assert.Equal(StreamErrorKind.AuthFailed, error.Kind);
        Assert.Equal(401, error.RtspStatus);
        Assert.DoesNotContain(WrongPassword, error.Detail);
        Assert.DoesNotContain(Uri.EscapeDataString(WrongPassword), error.Detail);
        Assert.DoesNotContain("viewer:", error.Detail);
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.AuthFailed, TimeSpan.FromSeconds(5)));
        Assert.Equal(StreamErrorKind.AuthFailed, session.LastErrorKind);
    }

    [SkippableFact]
    public void Unpublished_path_is_NotFound()
    {
        using var session = Open(server.Url("missing"));
        var error = FirstError(session, TimeSpan.FromSeconds(10));
        Assert.Equal(StreamErrorKind.NotFound, error?.Kind);
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Reconnecting, TimeSpan.FromSeconds(3)));
    }

    [SkippableFact]
    public void Closed_port_is_Unreachable()
    {
        using var session = Open("rtsp://127.0.0.1:1/none");
        Assert.Equal(StreamErrorKind.Unreachable, FirstError(session, TimeSpan.FromSeconds(10))?.Kind);
    }
}
```

- [ ] **Step 3: Run to verify failure.** `dotnet test tests/CamaraWin.Media.Tests` → build fails.

- [ ] **Step 4: Implement.**

`FFmpegLog.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

/// <summary>
/// Replaces FFmpeg's stderr logger. Lines logged against a registered AVFormatContext (warning or worse)
/// are forwarded to that session's sink; everything else is dropped.
/// </summary>
public static unsafe class FFmpegLog
{
    static readonly ConcurrentDictionary<nint, Action<string>> Sinks = new();
    static readonly object Gate = new();
    static av_log_set_callback_callback? _callback; // rooted: FFmpeg keeps the function pointer

    public static void Install()
    {
        lock (Gate)
        {
            if (_callback is not null) return;
            _callback = OnLog;
            ffmpeg.av_log_set_callback(_callback);
        }
    }

    public static void Register(void* context, Action<string> sink) => Sinks[(nint)context] = sink;

    public static void Unregister(void* context) => Sinks.TryRemove((nint)context, out _);

    static void OnLog(void* avcl, int level, string format, byte* vl)
    {
        if (level > ffmpeg.AV_LOG_WARNING || avcl is null || !Sinks.TryGetValue((nint)avcl, out var sink)) return;
        const int size = 1024;
        var buffer = stackalloc byte[size];
        var printPrefix = 1;
        ffmpeg.av_log_format_line(avcl, level, format, vl, buffer, size, &printPrefix);
        var line = Marshal.PtrToStringUTF8((nint)buffer)?.Trim();
        if (string.IsNullOrEmpty(line)) return;
        try { sink(line); }
        catch (Exception) { /* never let a sink break FFmpeg's thread */ }
    }
}
```

In `FFmpegLoader.Initialize`, right after `DynamicallyLoadedBindings.Initialize();`, add `FFmpegLog.Install();`.

`RtspStatusParser.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CamaraWin.Media;

public static partial class RtspStatusParser
{
    [GeneratedRegex(@"failed:\s*(\d{3})")]
    private static partial Regex MethodFailed();

    [GeneratedRegex(@"RTSP/1\.\d\s+(\d{3})")]
    private static partial Regex StatusLine();

    /// <summary>RTSP error status (≥ 400) found in an FFmpeg log line, or null.</summary>
    public static int? Parse(string line)
    {
        var match = MethodFailed().Match(line);
        if (!match.Success) match = StatusLine().Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var status) && status >= 400 ? status : null;
    }
}
```

`CredentialSanitizer.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CamaraWin.Media;

public static partial class CredentialSanitizer
{
    [GeneratedRegex(@"([a-zA-Z][a-zA-Z0-9+.\-]*://)[^/\s@]*@")]
    private static partial Regex UserInfo();

    /// <summary>Removes "user:password@" from every URL in the text.</summary>
    public static string Sanitize(string text) => UserInfo().Replace(text, "$1");
}
```

`StreamErrorClassifier.cs`:

```csharp
using CamaraWin.Core;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public static class StreamErrorClassifier
{
    // FFmpeg maps Winsock errors to UCRT errno values on Windows (ETIMEDOUT observed as -138).
    const int Etimedout = 138, Econnrefused = 107, Ehostunreach = 110, Enetunreach = 118,
        Eaddrnotavail = 101, Econnreset = 108, Eacces = 13;

    public static StreamErrorKind Classify(int ffmpegCode, int? rtspStatus, bool wasPlaying, bool deadlineHit)
    {
        if (rtspStatus is { } status)
        {
            return status switch
            {
                401 or 403 => StreamErrorKind.AuthFailed,
                404 or 454 => StreamErrorKind.NotFound,
                453 or 503 => StreamErrorKind.CameraBusy,
                >= 500 => StreamErrorKind.ServerError,
                _ => StreamErrorKind.Unknown,
            };
        }
        if (ffmpegCode == ffmpeg.AVERROR_HTTP_UNAUTHORIZED || ffmpegCode == ffmpeg.AVERROR_HTTP_FORBIDDEN || ffmpegCode == -Eacces)
            return StreamErrorKind.AuthFailed;
        if (ffmpegCode == ffmpeg.AVERROR_HTTP_NOT_FOUND) return StreamErrorKind.NotFound;
        if (ffmpegCode == ffmpeg.AVERROR_HTTP_SERVER_ERROR) return StreamErrorKind.ServerError;
        if (deadlineHit || ffmpegCode == ffmpeg.AVERROR_EXIT)
            return wasPlaying ? StreamErrorKind.Stalled : StreamErrorKind.Unreachable;
        if (-ffmpegCode is Etimedout or Econnrefused or Ehostunreach or Enetunreach or Eaddrnotavail or Econnreset
            || ffmpegCode == ffmpeg.AVERROR_EOF)
            return StreamErrorKind.Unreachable;
        return StreamErrorKind.Unknown;
    }
}
```

`StreamSession.cs` changes (keep everything else):

1. Fields:

```csharp
    const int SlowRetrySeconds = 30;
    volatile bool _deadlineHit;
    volatile int _lastRtspStatus;          // 0 = none, for the current attempt
    volatile string? _lastLogLine;
```

2. Replace the interrupt lambda in the constructor:

```csharp
        _interrupt = _ =>
        {
            if (_stopping) return 1;
            if (Environment.TickCount64 <= Interlocked.Read(ref _deadline)) return 0;
            _deadlineHit = true;
            return 1;
        };
```

3. Public members:

```csharp
    public StreamErrorKind? LastErrorKind { get; private set; }
    /// <summary>Raised on the session thread for every failed attempt or stall; subscriber exceptions are isolated.</summary>
    public event Action<StreamError>? ErrorOccurred;
```

4. Replace the `try/catch` block inside `Run`'s loop and the retry wait with:

```csharp
                var reachedPlaying = false;
                StreamError? error = null;
                try
                {
                    PlayOnce(hwDevice, ref reachedPlaying);
                }
                catch (Exception ex) when (!_stopping)
                {
                    error = BuildError(ex, reachedPlaying);
                }
                catch (Exception)
                {
                    // stopping: interruption is expected
                }
                if (_stopping) break;
                if (error is not null)
                {
                    Report(error);
                    if (error.Kind == StreamErrorKind.AuthFailed)
                    {
                        SetState(SessionState.AuthFailed);
                        return;
                    }
                }
                if (reachedPlaying) backoff = 1;
                SetState(SessionState.Reconnecting);
                var wait = error?.Kind is StreamErrorKind.NotFound or StreamErrorKind.CameraBusy ? SlowRetrySeconds : backoff;
                _stopSignal.Wait(TimeSpan.FromSeconds(wait));
                if (wait == backoff) backoff = Math.Min(backoff * 2, MaxBackoffSeconds);
```

(The `using CamaraWin.Core;` import is needed at the top.)

5. Helpers:

```csharp
    StreamError BuildError(Exception ex, bool wasPlaying)
    {
        var code = ex is FFmpegException f ? f.ErrorCode : 0;
        int? status = _lastRtspStatus == 0 ? null : _lastRtspStatus;
        var kind = StreamErrorClassifier.Classify(code, status, wasPlaying, _deadlineHit);
        var detail = _lastLogLine is { } line ? $"{ex.Message} — {line}" : ex.Message;
        return new StreamError(kind, status, code, CredentialSanitizer.Sanitize(detail), DateTime.UtcNow);
    }

    void Report(StreamError error)
    {
        LastError = error.Detail;
        LastErrorKind = error.Kind;
        try { ErrorOccurred?.Invoke(error); }
        catch (Exception) { /* isolated like StateChanged */ }
    }

    void OnFFmpegLog(string line)
    {
        _lastLogLine = line;
        if (RtspStatusParser.Parse(line) is { } status) _lastRtspStatus = status;
    }
```

6. In `PlayOnce`, right after `var fmt = ffmpeg.avformat_alloc_context();`:

```csharp
        var logContext = (nint)fmt; // FFmpeg frees fmt on a failed open; unregister by the original address
        _deadlineHit = false;
        _lastRtspStatus = 0;
        _lastLogLine = null;
        FFmpegLog.Register(fmt, OnFFmpegLog);
```

At the start of the `finally` block, add `FFmpegLog.Unregister((void*)logContext);`.

7. In `MarkPlaying`, where `LastError = null;` is set, also add `LastErrorKind = null;`.

- [ ] **Step 5: Run to verify pass.** `dotnet test tests/CamaraWin.Media.Tests` → all pass, including the existing `Wrong_password_ends_in_AuthFailed` and `Unreachable_camera_goes_to_Reconnecting`.
  - If `Wrong_password_is_AuthFailed_with_status_401…` fails because `RtspStatus` is null, print the captured log lines. The RTSP demuxer may log against a different context; if so, also register `fmt->priv_data` after a successful allocation. Report what you find.
  - If `Unpublished_path_is_NotFound` gets 401, the YAML permission was not applied; fix the fixture.
- [ ] **Step 6: Commit** `feat(media): classify connection errors from FFmpeg logs and codes; slow retry for not-found/busy` (with the trailer).

---

### Task 4: Media — stream stats

**Files:**
- Create: `src/CamaraWin.Media/StreamStats.cs`
- Modify: `src/CamaraWin.Media/StreamSession.cs`
- Test: `tests/CamaraWin.Media.Tests/StreamSessionTests.cs` (append)

**Interfaces:**
- Produces:
  - `sealed record StreamStats(double Fps, double LatencyMs, bool HardwareDecoding, TimeSpan SinceLastFrame)` with `static StreamStats Empty`.
  - `StreamStats StreamSession.Stats { get; }`, lock-free. `SinceLastFrame` is computed at read time and is `TimeSpan.Zero` before the first frame.

- [ ] **Step 1: Failing test** (append to `StreamSessionTests`):

```csharp
    [SkippableFact]
    public void Stats_report_fps_latency_and_freshness()
    {
        using var session = Open(server.Url("open"));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);
        Thread.Sleep(2500);
        var stats = session.Stats;
        output.WriteLine($"fps={stats.Fps:0.0} latency={stats.LatencyMs:0.0}ms hw={stats.HardwareDecoding} since={stats.SinceLastFrame.TotalMilliseconds:0}ms");
        Assert.InRange(stats.Fps, 15, 35);
        Assert.InRange(stats.LatencyMs, 0.01, 1000);
        Assert.True(stats.SinceLastFrame < TimeSpan.FromSeconds(1));
    }
```

- [ ] **Step 2: Run to verify failure** (the build fails because `Stats` is missing).

- [ ] **Step 3: Implement.** `StreamStats.cs`:

```csharp
namespace CamaraWin.Media;

/// <summary>Live-view health: frames per second, packet-received→frame-published latency, decoder, freshness.</summary>
public sealed record StreamStats(double Fps, double LatencyMs, bool HardwareDecoding, TimeSpan SinceLastFrame)
{
    public static StreamStats Empty { get; } = new(0, 0, false, TimeSpan.Zero);
}
```

In `StreamSession.cs` (add `using System.Diagnostics;`):

```csharp
    StreamStats _stats = StreamStats.Empty;
    long _lastFrameTimestamp;       // Stopwatch ticks, 0 = none
    long _statsWindowStart;
    int _statsFrames;
    double _fps;
    double _latencyEma = -1;
    long _packetTimestamp;          // when the packet being decoded was read

    public StreamStats Stats
    {
        get
        {
            var stats = Volatile.Read(ref _stats);
            var last = Interlocked.Read(ref _lastFrameTimestamp);
            return last == 0 ? stats : stats with { SinceLastFrame = Stopwatch.GetElapsedTime(last) };
        }
    }

    void UpdateStats(bool hardware)
    {
        var now = Stopwatch.GetTimestamp();
        var latency = Stopwatch.GetElapsedTime(_packetTimestamp, now).TotalMilliseconds;
        _latencyEma = _latencyEma < 0 ? latency : _latencyEma * 0.9 + latency * 0.1;
        _statsFrames++;
        if (_statsWindowStart == 0) _statsWindowStart = now;
        var window = Stopwatch.GetElapsedTime(_statsWindowStart, now).TotalSeconds;
        if (window >= 1)
        {
            _fps = _statsFrames / window;
            _statsFrames = 0;
            _statsWindowStart = now;
        }
        Interlocked.Exchange(ref _lastFrameTimestamp, now);
        Volatile.Write(ref _stats, new StreamStats(_fps, _latencyEma, hardware, TimeSpan.Zero));
    }
```

- In `PlayOnce`, right after a successful `av_read_frame`, set `_packetTimestamp = Stopwatch.GetTimestamp();`.
- In `PresentFrame`, capture `var hardware = frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11;` at the top and call `UpdateStats(hardware);` after `Mailbox.Publish(target);`.

- [ ] **Step 4: Run to verify pass.** `dotnet test tests/CamaraWin.Media.Tests --filter FullyQualifiedName~StreamSessionTests` → pass. Report the printed stats line, including `hw`.
- [ ] **Step 5: Commit** `feat(media): per-session fps, latency and decoder stats` (with the trailer).

---

### Task 5: Core — JSON backup (AES-GCM), merge, automatic copy, auto-start, example file

**Files:**
- Create: `src/CamaraWin.Core/Backup/CameraBackup.cs`, `Backup/BackupMerge.cs`, `Backup/BackupWriter.cs`, `src/CamaraWin.Core/AutoStart.cs`, `docs/ejemplo-camaras.json`
- Test: `tests/CamaraWin.Core.Tests/CameraBackupTests.cs`, `BackupMergeTests.cs`, `AutoStartTests.cs`

**Interfaces:**
- Produces (namespace `CamaraWin.Core`):
  - `sealed class BackupFormatException(string message) : Exception` and `sealed class BackupPassphraseException : Exception`.
  - `sealed record BackupImport(IReadOnlyList<Camera> Cameras, bool WasEncrypted)`.
  - `static class CameraBackup` with `const string Format = "camarawin-cameras"`, `const int Version = 1`, `const int Iterations = 600_000`, `string Export(IEnumerable<Camera> cameras, string? passphrase, DateTime? now = null)` and `BackupImport Import(string json, Func<string?> askPassphrase)`. When `askPassphrase` returns null, Import throws `OperationCanceledException`.
  - `sealed record MergeResult(IReadOnlyList<Camera> Cameras, int Added, int Updated, int WithoutPassword, IReadOnlySet<Guid> UpdatedIds)` and `static MergeResult BackupMerge.Merge(IReadOnlyList<Camera> existing, IReadOnlyList<Camera> imported)`.
  - `static class BackupWriter` with `const string AutomaticFileName = "camaras-copia.json"` and `static string WriteAutomatic(string folder, IEnumerable<Camera> cameras)`, which returns the path.
  - `interface IRunKey { string? Get(string name); void Set(string name, string value); void Delete(string name); }`, `sealed class RegistryRunKey : IRunKey`, and `sealed class AutoStart(IRunKey key, string exePath)` with `string Command`, `bool IsEnabled`, `void Enable()`, `void Disable()`.

- [ ] **Step 1: Failing tests.** `CameraBackupTests.cs`:

```csharp
using System.Text.Json.Nodes;
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class CameraBackupTests
{
    static List<Camera> Sample() =>
    [
        new() { Name = "Garaje", Brand = Brand.Tapo, Host = "192.168.1.20", Port = 554, User = "camuser", Password = "Secreta#1", Order = 0 },
        new() { Name = "Jardín", Brand = Brand.Imou, Host = "192.168.1.30", Port = 554, User = "admin", Password = "L2ABCD", Order = 1, UseUdp = true },
        new() { Name = "Otra", Brand = Brand.Custom, MainUrlOverride = "rtsp://h/x", Order = 2 },
    ];

    [Fact]
    public void Round_trip_without_passphrase_drops_passwords()
    {
        var json = CameraBackup.Export(Sample(), null);
        Assert.DoesNotContain("Secreta#1", json);
        var result = CameraBackup.Import(json, () => throw new InvalidOperationException("must not ask"));
        Assert.False(result.WasEncrypted);
        Assert.Equal(new[] { "Garaje", "Jardín", "Otra" }, result.Cameras.Select(c => c.Name).ToArray());
        Assert.All(result.Cameras, c => Assert.Equal("", c.Password));
        Assert.True(result.Cameras[1].UseUdp);
        Assert.Equal("rtsp://h/x", result.Cameras[2].MainUrlOverride);
    }

    [Fact]
    public void Round_trip_with_passphrase_restores_passwords()
    {
        var json = CameraBackup.Export(Sample(), "clave-larga");
        Assert.DoesNotContain("Secreta#1", json);
        Assert.DoesNotContain("L2ABCD", json);
        var result = CameraBackup.Import(json, () => "clave-larga");
        Assert.True(result.WasEncrypted);
        Assert.Equal(new[] { "Secreta#1", "L2ABCD", "" }, result.Cameras.Select(c => c.Password).ToArray());
    }

    [Fact]
    public void Wrong_passphrase_throws() =>
        Assert.Throws<BackupPassphraseException>(() =>
            CameraBackup.Import(CameraBackup.Export(Sample(), "clave-larga"), () => "otra-clave"));

    [Fact]
    public void Cancelled_passphrase_throws_OperationCanceled() =>
        Assert.Throws<OperationCanceledException>(() =>
            CameraBackup.Import(CameraBackup.Export(Sample(), "clave-larga"), () => null));

    [Fact]
    public void Swapped_encrypted_passwords_fail_authentication()
    {
        var node = JsonNode.Parse(CameraBackup.Export(Sample(), "clave-larga"))!;
        var cams = node["cameras"]!.AsArray();
        (cams[0]!["password"], cams[1]!["password"]) = (cams[1]!["password"]!.DeepClone(), cams[0]!["password"]!.DeepClone());
        Assert.Throws<BackupPassphraseException>(() => CameraBackup.Import(node.ToJsonString(), () => "clave-larga"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "format": "otra-cosa", "version": 1, "cameras": [] }""")]
    [InlineData("""{ "format": "camarawin-cameras", "version": 2, "cameras": [] }""")]
    public void Foreign_corrupt_or_future_files_throw_format(string json) =>
        Assert.Throws<BackupFormatException>(() => CameraBackup.Import(json, () => "x"));

    [Fact]
    public void Credentials_in_override_urls_are_not_exported()
    {
        var cam = new Camera { Name = "U", Brand = Brand.Custom, MainUrlOverride = "rtsp://bob:s3cret@h/x" };
        Assert.DoesNotContain("s3cret", CameraBackup.Export([cam], null));
    }

    [Fact]
    public void Example_file_imports()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CamaraWin.slnx"))) dir = dir.Parent;
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "ejemplo-camaras.json"));
        Assert.Equal(2, CameraBackup.Import(json, () => null).Cameras.Count);
    }
}
```

`BackupMergeTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class BackupMergeTests
{
    [Fact]
    public void Matches_by_host_and_port_updates_and_appends()
    {
        var existing = new List<Camera>
        {
            new() { Name = "Viejo", Host = "192.168.1.20", Port = 554, Password = "keep", Order = 0 },
            new() { Name = "Otro", Host = "192.168.1.40", Port = 554, Password = "p", Order = 1 },
        };
        var imported = new List<Camera>
        {
            new() { Name = "Garaje", Host = "192.168.1.20", Port = 554, Password = "", User = "u" },
            new() { Name = "Nuevo", Host = "192.168.1.50", Port = 554, Password = "" },
        };
        var r = BackupMerge.Merge(existing, imported);
        Assert.Equal((1, 1, 1), (r.Added, r.Updated, r.WithoutPassword));
        var updated = r.Cameras.Single(c => c.Id == existing[0].Id);
        Assert.Equal(("Garaje", "u", "keep", 0), (updated.Name, updated.User, updated.Password, updated.Order));
        Assert.Contains(existing[0].Id, r.UpdatedIds);
        Assert.Equal(2, r.Cameras.Single(c => c.Name == "Nuevo").Order);
        Assert.Equal("p", existing[1].Password); // originals untouched
        Assert.Equal("Viejo", existing[0].Name);
    }

    [Fact]
    public void Imported_password_replaces_existing()
    {
        var existing = new List<Camera> { new() { Host = "h", Port = 554, Password = "old" } };
        var r = BackupMerge.Merge(existing, [new Camera { Host = "H", Port = 554, Password = "new" }]);
        Assert.Equal("new", r.Cameras[0].Password);
    }

    [Fact]
    public void Custom_cameras_match_by_main_url()
    {
        var existing = new List<Camera> { new() { Brand = Brand.Custom, MainUrlOverride = "rtsp://h/x" } };
        var r = BackupMerge.Merge(existing, [new Camera { Brand = Brand.Custom, MainUrlOverride = "RTSP://h/x", Name = "N" }]);
        Assert.Equal((0, 1), (r.Added, r.Updated));
    }
}
```

`AutoStartTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class AutoStartTests
{
    sealed class FakeRunKey : IRunKey
    {
        public readonly Dictionary<string, string> Values = [];
        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    [Fact]
    public void Enable_writes_quoted_command_with_tray_flag_and_disable_removes_it()
    {
        var key = new FakeRunKey();
        var auto = new AutoStart(key, @"C:\Apps\CamaraWin\CamaraWin.exe");
        Assert.False(auto.IsEnabled);
        auto.Enable();
        Assert.Equal("\"C:\\Apps\\CamaraWin\\CamaraWin.exe\" --tray", key.Values["CamaraWin"]);
        Assert.True(auto.IsEnabled);
        auto.Disable();
        Assert.False(auto.IsEnabled);
        Assert.Empty(key.Values);
    }

    [Fact]
    public void Entry_for_another_path_is_not_enabled()
    {
        var key = new FakeRunKey();
        key.Set("CamaraWin", "\"D:\\old\\CamaraWin.exe\" --tray");
        Assert.False(new AutoStart(key, @"C:\new\CamaraWin.exe").IsEnabled);
    }
}
```

Add to `CameraBackupTests` a test for the automatic writer:

```csharp
    [Fact]
    public void Automatic_copy_has_no_passwords_and_replaces_previous()
    {
        var dir = Path.Combine(Path.GetTempPath(), "camarawin-bk-" + Guid.NewGuid());
        try
        {
            BackupWriter.WriteAutomatic(dir, Sample());
            var path = BackupWriter.WriteAutomatic(dir, Sample().Take(1));
            var text = File.ReadAllText(path);
            Assert.Equal(Path.Combine(dir, BackupWriter.AutomaticFileName), path);
            Assert.DoesNotContain("Secreta#1", text);
            Assert.Single(CameraBackup.Import(text, () => null).Cameras);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
```

- [ ] **Step 2: Run to verify failure** (the build fails).

- [ ] **Step 3: Implement.** `Backup/CameraBackup.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamaraWin.Core;

public sealed class BackupFormatException(string message) : Exception(message);

public sealed class BackupPassphraseException() : Exception("La clave no es correcta o el archivo está dañado.");

public sealed record BackupImport(IReadOnlyList<Camera> Cameras, bool WasEncrypted);

public static class CameraBackup
{
    public const string Format = "camarawin-cameras";
    public const int Version = 1;
    public const int Iterations = 600_000;
    const int SaltSize = 16, KeySize = 32, NonceSize = 12, TagSize = 16;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Export(IEnumerable<Camera> cameras, string? passphrase, DateTime? now = null)
    {
        byte[]? salt = null, key = null;
        if (!string.IsNullOrEmpty(passphrase))
        {
            salt = RandomNumberGenerator.GetBytes(SaltSize);
            key = DeriveKey(passphrase, salt);
        }
        var file = new BackupFile
        {
            Format = Format,
            Version = Version,
            ExportedAt = (now ?? DateTime.UtcNow).ToUniversalTime(),
            Encryption = salt is null ? null : new EncryptionInfo { Salt = Convert.ToBase64String(salt) },
            Cameras = cameras.OrderBy(c => c.Order).Select(c => new BackupCamera
            {
                Name = c.Name,
                Brand = c.Brand,
                Host = c.Host,
                Port = c.Port,
                User = c.User,
                Password = key is null || c.Password.Length == 0 ? null : Encrypt(key, c.Password, Aad(c.Name, c.Host, c.Port)),
                MainUrl = Strip(c.MainUrlOverride),
                SubUrl = Strip(c.SubUrlOverride),
                UseUdp = c.UseUdp,
            }).ToList(),
        };
        return JsonSerializer.Serialize(file, Json);
    }

    public static BackupImport Import(string json, Func<string?> askPassphrase)
    {
        BackupFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BackupFile>(json, Json);
        }
        catch (JsonException)
        {
            throw new BackupFormatException("El archivo está dañado o no es un JSON válido.");
        }
        if (file is null || file.Format != Format)
            throw new BackupFormatException("El archivo no es una copia de cámaras de CamaraWin.");
        if (file.Version > Version)
            throw new BackupFormatException("Esta copia se hizo con una versión más nueva de CamaraWin. Actualiza la aplicación.");

        byte[]? key = null;
        if (file.Encryption is { } encryption)
        {
            var passphrase = askPassphrase() ?? throw new OperationCanceledException();
            key = DeriveKey(passphrase, Convert.FromBase64String(encryption.Salt));
        }

        var cameras = new List<Camera>();
        foreach (var (entry, index) in (file.Cameras ?? []).Select((e, i) => (e, i)))
        {
            var name = string.IsNullOrWhiteSpace(entry.Name) ? $"Cámara {index + 1}" : entry.Name;
            var host = entry.Host ?? "";
            var port = entry.Port is > 0 and <= 65535 ? entry.Port : 554;
            cameras.Add(new Camera
            {
                Name = name,
                Brand = Enum.IsDefined(entry.Brand) ? entry.Brand : Brand.Custom,
                Host = host,
                Port = port,
                User = entry.User ?? "",
                Password = key is null || entry.Password is null ? "" : Decrypt(key, entry.Password, Aad(entry.Name ?? "", host, entry.Port)),
                MainUrlOverride = entry.MainUrl,
                SubUrlOverride = entry.SubUrl,
                UseUdp = entry.UseUdp,
                Order = index,
            });
        }
        return new BackupImport(cameras, key is not null);
    }

    static string? Strip(string? url) => url is null ? null : StreamUrlBuilder.StripCredentials(url, out _);

    static string Aad(string name, string host, int port) => $"{name}|{host}|{port}";

    static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    static string Encrypt(byte[] key, string plaintext, string aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(aad));
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    static string Decrypt(byte[] key, string payload, string aad)
    {
        try
        {
            var bytes = Convert.FromBase64String(payload);
            if (bytes.Length < NonceSize + TagSize) throw new BackupPassphraseException();
            var nonce = bytes.AsSpan(0, NonceSize);
            var tag = bytes.AsSpan(bytes.Length - TagSize);
            var cipher = bytes.AsSpan(NonceSize, bytes.Length - NonceSize - TagSize);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(aad));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            throw new BackupPassphraseException();
        }
    }

    sealed class BackupFile
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public DateTime ExportedAt { get; set; }
        public EncryptionInfo? Encryption { get; set; }
        public List<BackupCamera>? Cameras { get; set; }
    }

    sealed class EncryptionInfo
    {
        public string Algorithm { get; set; } = "AES-256-GCM";
        public string Kdf { get; set; } = "PBKDF2-SHA256";
        public int Iterations { get; set; } = CameraBackup.Iterations;
        public string Salt { get; set; } = "";
    }

    sealed class BackupCamera
    {
        public string? Name { get; set; }
        public Brand Brand { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 554;
        public string? User { get; set; }
        public string? Password { get; set; }
        public string? MainUrl { get; set; }
        public string? SubUrl { get; set; }
        public bool UseUdp { get; set; }
    }
}
```

On import, the associated data uses `entry.Name ?? ""` and `entry.Port` exactly as stored, so it matches what Export used.

Before implementing Import, check that `EncryptionInfo.Iterations` from the file matches `Iterations`. If it differs, throw `BackupFormatException("Parámetros de cifrado no soportados.")`. Add a test for that case.

`Backup/BackupMerge.cs`:

```csharp
namespace CamaraWin.Core;

public sealed record MergeResult(IReadOnlyList<Camera> Cameras, int Added, int Updated, int WithoutPassword, IReadOnlySet<Guid> UpdatedIds);

public static class BackupMerge
{
    /// <summary>Returns a new list (clones); inputs are not modified.</summary>
    public static MergeResult Merge(IReadOnlyList<Camera> existing, IReadOnlyList<Camera> imported)
    {
        var result = existing.Select(c => c.Clone()).ToList();
        var updatedIds = new HashSet<Guid>();
        var touched = new List<Camera>();
        var added = 0;
        var nextOrder = result.Count == 0 ? 0 : result.Max(c => c.Order) + 1;
        foreach (var incoming in imported)
        {
            var match = result.FirstOrDefault(c => SameCamera(c, incoming));
            if (match is null)
            {
                var copy = incoming.Clone();
                copy.Id = Guid.NewGuid();
                copy.Order = nextOrder++;
                result.Add(copy);
                touched.Add(copy);
                added++;
                continue;
            }
            match.Name = incoming.Name;
            match.Brand = incoming.Brand;
            match.Host = incoming.Host;
            match.Port = incoming.Port;
            match.User = incoming.User;
            if (incoming.Password.Length > 0) match.Password = incoming.Password;
            match.MainUrlOverride = incoming.MainUrlOverride;
            match.SubUrlOverride = incoming.SubUrlOverride;
            match.UseUdp = incoming.UseUdp;
            updatedIds.Add(match.Id);
            touched.Add(match);
        }
        return new MergeResult(result, added, updatedIds.Count, touched.Count(c => c.Password.Length == 0), updatedIds);
    }

    static bool SameCamera(Camera a, Camera b) =>
        a.Host.Length > 0 && b.Host.Length > 0
            ? string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port
            : a.Host.Length == 0 && b.Host.Length == 0 && a.MainUrlOverride is { } x && b.MainUrlOverride is { } y
              && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
```

`Backup/BackupWriter.cs`:

```csharp
namespace CamaraWin.Core;

public static class BackupWriter
{
    public const string AutomaticFileName = "camaras-copia.json";

    /// <summary>Writes the password-less automatic copy atomically and returns its path.</summary>
    public static string WriteAutomatic(string folder, IEnumerable<Camera> cameras)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, AutomaticFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, CameraBackup.Export(cameras, passphrase: null));
        File.Move(tmp, path, overwrite: true);
        return path;
    }
}
```

`AutoStart.cs`:

```csharp
using Microsoft.Win32;

namespace CamaraWin.Core;

public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run — no admin rights needed.</summary>
public sealed class RegistryRunKey : IRunKey
{
    const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, value);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class AutoStart(IRunKey key, string exePath)
{
    const string ValueName = "CamaraWin";

    public string Command => $"\"{exePath}\" --tray";
    public bool IsEnabled => string.Equals(key.Get(ValueName), Command, StringComparison.OrdinalIgnoreCase);
    public void Enable() => key.Set(ValueName, Command);
    public void Disable() => key.Delete(ValueName);
}
```

`docs/ejemplo-camaras.json`:

```json
{
  "format": "camarawin-cameras",
  "version": 1,
  "exportedAt": "2026-09-26T21:40:00Z",
  "encryption": null,
  "cameras": [
    {
      "name": "Garaje",
      "brand": "Tapo",
      "host": "192.168.1.20",
      "port": 554,
      "user": "camuser",
      "password": null,
      "mainUrl": null,
      "subUrl": null,
      "useUdp": false
    },
    {
      "name": "Jardín",
      "brand": "Imou",
      "host": "192.168.1.30",
      "port": 554,
      "user": "admin",
      "password": null,
      "mainUrl": null,
      "subUrl": null,
      "useUdp": false
    }
  ]
}
```

- [ ] **Step 4: Run to verify pass.** `dotnet test tests/CamaraWin.Core.Tests` → all pass. The PBKDF2 tests take a few seconds; that is expected.
- [ ] **Step 5: Commit** `feat(core): encrypted JSON backup, merge, automatic copy and auto-start` (with the trailer).

---

### Task 6: App — move recordings out of tiles into RecordingController

Behaviour must stay the same as v1. This refactor exists so that layout changes and tray mode never stop a recording.

**Files:**
- Create: `src/CamaraWin.App/RecordingController.cs`
- Modify: `src/CamaraWin.App/CameraTile.xaml.cs`, `src/CamaraWin.App/MainWindow.xaml.cs`, `src/CamaraWin.App/FullscreenWindow.xaml.cs`

**Interfaces:**
- Produces:
  - `enum RecordingStatus { Off, Connecting, Recording, Paused }`.
  - `sealed class RecordingController(Dispatcher dispatcher)` (UI-thread affine) with:
    - `event Action<Guid, RecordingStatus>? StatusChanged`
    - `event Action<Camera, StreamError>? ErrorOccurred`
    - `event Action<Camera, SessionState>? SessionStateChanged`
    - `event Action<string, string?>? Notify`
    - `bool IsRecording(Guid)`, `bool AnyRecording`, `RecordingStatus StatusOf(Guid)`
    - `void Start(Camera)`, `Task Stop(Guid cameraId)`, `void StopWithNotice(Guid cameraId)`, `void StopAllWithNotice()`
    - `Task ShutdownAsync()`
  - `CameraTile` now:
    - raises `event Action<CameraTile>? RecordRequested`
    - has `void SetRecordingStatus(RecordingStatus status)` for the dot and button
    - no longer owns recordings (`IsRecording`, `_recording`, `_finalizing` and `StopRecording` are removed)
    - `ShutdownAsync` only stops the live session.
  - `FullscreenWindow` exposes `internal CameraTile Tile`; MainWindow wires it like grid tiles.

- [ ] **Step 1: Create `RecordingController.cs`:**

```csharp
using System.Windows.Threading;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public enum RecordingStatus { Off, Connecting, Recording, Paused }

/// <summary>
/// Owns one headless mainstream recording session per camera, independent of which tiles are on
/// screen (layout changes and tray mode never stop a recording). Call from the UI thread only.
/// </summary>
public sealed class RecordingController(Dispatcher dispatcher)
{
    sealed class Entry(Camera camera, StreamSession session)
    {
        public Camera Camera { get; } = camera;
        public StreamSession Session { get; } = session;
        public RecordingStatus Status { get; set; } = RecordingStatus.Connecting;
    }

    readonly Dictionary<Guid, Entry> _active = [];
    readonly List<Task> _finalizing = [];

    public event Action<Guid, RecordingStatus>? StatusChanged;
    public event Action<Camera, StreamError>? ErrorOccurred;
    public event Action<Camera, SessionState>? SessionStateChanged;
    public event Action<string, string?>? Notify;

    public bool IsRecording(Guid cameraId) => _active.ContainsKey(cameraId);
    public bool AnyRecording => _active.Count > 0;
    public RecordingStatus StatusOf(Guid cameraId) => _active.TryGetValue(cameraId, out var e) ? e.Status : RecordingStatus.Off;

    public void Start(Camera camera)
    {
        if (_active.ContainsKey(camera.Id)) return;
        string url;
        try { url = StreamUrlBuilder.Build(camera, StreamKind.Main); }
        catch (InvalidOperationException)
        {
            Notify?.Invoke($"No se puede grabar {camera.Name}: falta la URL RTSP.", null);
            return;
        }
        var name = camera.Name;
        var session = new StreamSession(url, camera.UseUdp, decode: false);
        var entry = new Entry(camera, session);
        session.StartRecording(() => AppPaths.RecordingFile(name, DateTime.Now));
        session.RecordingFailed += message => dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(entry)) return;
            _ = Stop(camera.Id);
            Notify?.Invoke($"Grabación de {name} detenida: {message}", null);
        });
        session.ErrorOccurred += error => dispatcher.BeginInvoke(() =>
        {
            if (IsCurrent(entry)) ErrorOccurred?.Invoke(camera, error);
        });
        session.StateChanged += state => dispatcher.BeginInvoke(() =>
        {
            if (!IsCurrent(entry)) return;
            SessionStateChanged?.Invoke(camera, state);
            if (state == SessionState.AuthFailed)
            {
                _ = Stop(camera.Id);
                Notify?.Invoke($"No se pudo grabar {name}: contraseña incorrecta.", null);
                return;
            }
            var status = state switch
            {
                SessionState.Playing => RecordingStatus.Recording,
                SessionState.Reconnecting => RecordingStatus.Paused,
                _ => entry.Status,
            };
            if (status == entry.Status) return;
            var pausedNow = status == RecordingStatus.Paused;
            entry.Status = status;
            StatusChanged?.Invoke(camera.Id, status);
            if (pausedNow) Notify?.Invoke($"Grabación de {name} en pausa: reconectando…", null);
        });
        _active[camera.Id] = entry;
        session.Start();
        StatusChanged?.Invoke(camera.Id, RecordingStatus.Connecting);
        Notify?.Invoke($"Grabando {name}…", null);
    }

    /// <summary>Stops and finalizes in the background; the task faults with RecordingException if the file could not be closed.</summary>
    public Task Stop(Guid cameraId)
    {
        if (!_active.Remove(cameraId, out var entry)) return Task.CompletedTask;
        StatusChanged?.Invoke(cameraId, RecordingStatus.Off);
        entry.Session.RequestStop();
        var finalize = Task.Run(() => FinalizeAsync(entry.Session));
        _finalizing.Add(finalize);
        finalize.ContinueWith(_ => dispatcher.BeginInvoke(() => _finalizing.Remove(finalize)), TaskScheduler.Default);
        return finalize;
    }

    /// <summary>Stop with user-facing "guardando…" / "guardada" / error notices.</summary>
    public void StopWithNotice(Guid cameraId)
    {
        if (!_active.TryGetValue(cameraId, out var entry)) return;
        var name = entry.Camera.Name;
        Notify?.Invoke($"Guardando grabación de {name}…", null);
        Stop(cameraId).ContinueWith(done => dispatcher.BeginInvoke(() =>
        {
            if (done.Exception is { } ex)
                Notify?.Invoke($"No se pudo cerrar la grabación de {name}: {ex.GetBaseException().Message}", null);
            else
                Notify?.Invoke($"Grabación de {name} guardada en {AppPaths.RecordingsDirectory}", AppPaths.RecordingsDirectory);
        }), TaskScheduler.Default);
    }

    public void StopAllWithNotice()
    {
        foreach (var id in _active.Keys.ToList()) StopWithNotice(id);
    }

    /// <summary>For app exit: stops every recording; the task completes when all files are finalized.</summary>
    public Task ShutdownAsync()
    {
        foreach (var id in _active.Keys.ToList()) _ = Stop(id);
        return Task.WhenAll(_finalizing.ToArray());
    }

    bool IsCurrent(Entry entry) => _active.TryGetValue(entry.Camera.Id, out var current) && ReferenceEquals(current, entry);

    static async Task FinalizeAsync(StreamSession session)
    {
        try { await session.StopRecordingAsync().ConfigureAwait(false); }
        finally { session.Dispose(); }
    }
}
```

The "Stop(): RequestStop before StopRecordingAsync" order matches v1 `CameraTile.ShutdownAsync`. Stop finalizes the active recorder, and `Dispose` joins the thread.

- [ ] **Step 2: Refactor `CameraTile.xaml.cs`:**
  - Delete `_recording`, `_finalizing`, `_recordingPauseNotified`, `IsRecording`, `StopRecording`, `FinalizeRecordingAsync`, `ShowRecordingState` and the body of `Record_Click`.
  - Add:

```csharp
    public event Action<CameraTile>? RecordRequested;

    void Record_Click(object sender, RoutedEventArgs e) => RecordRequested?.Invoke(this);

    public void SetRecordingStatus(RecordingStatus status)
    {
        if (status == RecordingStatus.Off)
        {
            RecDot.Visibility = Visibility.Collapsed;
            RecordButton.Content = "⏺";
            RecordButton.ToolTip = "Grabar";
            return;
        }
        ShowRecordingDot(paused: status != RecordingStatus.Recording, status switch
        {
            RecordingStatus.Recording => "Grabando",
            RecordingStatus.Paused => "Grabación en pausa: reconectando…",
            _ => "Grabación: conectando…",
        });
        RecordButton.Content = "⏹";
        RecordButton.ToolTip = "Detener grabación";
    }
```

  - Replace `ShutdownAsync` with a version that only stops the live session:

```csharp
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        var session = _session;
        session?.RequestStop();
        _shutdown = session is null ? Task.CompletedTask : Task.Run(session.Dispose);
        return _shutdown;
    }
```

- [ ] **Step 3: Wire MainWindow.**
  - Add the field `readonly RecordingController _recordings;`. Initialize it in the constructor, before `RebuildGrid()`:

```csharp
        _recordings = new RecordingController(Dispatcher);
        _recordings.Notify += Notify;
        _recordings.StatusChanged += (id, status) =>
        {
            foreach (var tile in TilesOf(id)) tile.SetRecordingStatus(status);
        };
```

  - Add the helper `IEnumerable<CameraTile> TilesOf(Guid id)`, which yields the grid tiles for that camera plus the `Tile` of any open `FullscreenWindow` for that camera (iterate `OwnedWindows.OfType<FullscreenWindow>()`).
  - In `CreateTile`, subscribe `tile.RecordRequested += t => ToggleRecording(t.Camera);` and call `tile.SetRecordingStatus(_recordings.StatusOf(camera.Id));`.
  - Add:

```csharp
    void ToggleRecording(Camera camera)
    {
        if (_recordings.IsRecording(camera.Id)) _recordings.StopWithNotice(camera.Id);
        else _recordings.Start(camera);
    }
```

  - `EditCamera`: before replacing the camera, `if (_recordings.IsRecording(id)) _recordings.StopWithNotice(id);`, because the URL may change. `DeleteCamera` does the same.
  - `DisposeTile` / `TrackShutdown`: drop the `wasRecording` logic, since tiles no longer record. The new signature is `TrackShutdown(Task shutdown, string name)`: it only tracks the task and reports failures as "Error al detener la cámara {name}: …".
  - `ShowFullscreen`: wire `window.Tile.RecordRequested`, `window.Tile.Notify` and `window.Tile.SetRecordingStatus(...)` the same way, and track `window.Tile.ShutdownAsync()` on Closed.
  - `OnClosed`: add `_recordings.ShutdownAsync()` to the array of tasks waited on with the existing 8 s bound.

- [ ] **Step 4: FullscreenWindow.** Expose `internal CameraTile Tile => _tile;`. Remove `TileIsRecording`, remove its own Notify wiring (the owner wires it), and keep Esc / double-click close.
- [ ] **Step 5: Build and smoke-check.**
  - `dotnet build CamaraWin.slnx` → 0 warnings.
  - `dotnet test CamaraWin.slnx` → green.
  - Smoke run as in v1: seed `%APPDATA%\CamaraWin` only if absent; mediamtx from a scratch folder; one publisher at `rtsp://127.0.0.1:8554/cam1`; launch; alive 8 s; `CloseMainWindow`; exit within 5 s; clean up.
- [ ] **Step 6: Commit** `refactor(app): recordings owned by a window-level RecordingController` (with the trailer).

---

### Task 7: App — featured layout ("Principal + miniaturas")

**Files:**
- Create: `src/CamaraWin.App/MainWindow.View.cs` (partial)
- Modify: `MainWindow.xaml` (`UniformGrid` → `Grid`; add a combo item), `MainWindow.xaml.cs` (move `RebuildGrid`, `CreateTile` and `DisposeTile` into the new partial; the tile map key becomes `(Guid, StreamKind)`), `CameraTile.xaml.cs` (`FirstFrameShown`, `Clicked`)

**Interfaces:**
- Consumes: `ViewPlanner.Plan`, `TileSlot`, `LayoutMode`, `AppSettings.LayoutMode`/`FeaturedCameraId` (Task 1); `RecordingController` (Task 6).
- Produces: `CameraTile.FirstFrameShown` (`event Action<CameraTile>`, raised once, on the UI thread, after the first frame is painted) and `CameraTile.Clicked` (`event Action<CameraTile>`, single click without drag); `MainWindow.RebuildView()` replaces `RebuildGrid()` everywhere.

- [ ] **Step 1: CameraTile additions.**

```csharp
    bool _firstFrameRaised;
    public event Action<CameraTile>? FirstFrameShown;
    public event Action<CameraTile>? Clicked;
```

In `OnRendering`, set a local `var painted = false;` before `TryRead`, set `painted = true` inside the lambda after `WritePixels`, and after `TryRead` add:

```csharp
        if (painted && !_firstFrameRaised)
        {
            _firstFrameRaised = true;
            FirstFrameShown?.Invoke(this);
        }
```

Add a click handler:

```csharp
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // _dragStart is cleared when a drag starts or on double-click, so reaching here with it set means a plain click.
        if (_dragStart is null) return;
        _dragStart = null;
        Clicked?.Invoke(this);
    }
```

(The first click of a double-click on a thumbnail also features that camera before the fullscreen opens. This is accepted.)

- [ ] **Step 2: XAML.**
  - In `MainWindow.xaml`, replace `<UniformGrid x:Name="TileGrid"/>` with `<Grid x:Name="TileGrid"/>`.
  - Add `<ComboBoxItem Content="Principal + miniaturas"/>` as the 6th combo item, and widen the combo to `Width="170"`.

- [ ] **Step 3: `MainWindow.View.cs`** (move the view code here; key the tile map by camera and stream kind):

```csharp
using System.Windows;
using System.Windows.Controls;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class MainWindow
{
    readonly Dictionary<(Guid Id, StreamKind Kind), CameraTile> _tiles = [];
    // Old-stream tiles kept visible over a new tile until the new one shows its first frame.
    readonly Dictionary<CameraTile, CameraTile> _placeholders = [];   // new tile → placeholder

    const int FeaturedIndex = 5;

    void RebuildView()
    {
        var plan = ViewPlanner.Plan(_cameras, _settings.LayoutMode, _settings.GridMode, _settings.FeaturedCameraId);
        TileGrid.RowDefinitions.Clear();
        TileGrid.ColumnDefinitions.Clear();
        for (var r = 0; r < plan.Rows; r++) TileGrid.RowDefinitions.Add(new RowDefinition());
        for (var c = 0; c < plan.Columns; c++) TileGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var wanted = plan.Slots.Select(s => (s.CameraId, s.Kind)).ToHashSet();
        foreach (var slot in plan.Slots) PlaceTile(slot, wanted);
        foreach (var key in _tiles.Keys.Where(k => !wanted.Contains(k)).ToList()) DisposeTile(key);
        EmptyState.Visibility = _cameras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void PlaceTile(TileSlot slot, HashSet<(Guid, StreamKind)> wanted)
    {
        var key = (slot.CameraId, slot.Kind);
        if (_tiles.TryGetValue(key, out var existing))
        {
            Position(existing, slot);
            return;
        }
        var camera = _cameras.First(c => c.Id == slot.CameraId);
        var tile = CreateTile(camera, slot.Kind);
        _tiles[key] = tile;
        Position(tile, slot);
        TileGrid.Children.Add(tile);

        // Same camera already on screen with the other stream (e.g. just promoted/demoted): keep showing
        // it on top of the new tile until the new stream has a frame, so the swap never flashes black.
        var otherKey = (slot.CameraId, slot.Kind == StreamKind.Main ? StreamKind.Sub : StreamKind.Main);
        if (!wanted.Contains(otherKey) && _tiles.Remove(otherKey, out var placeholder))
        {
            Position(placeholder, slot);
            Panel.SetZIndex(placeholder, 1);
            _placeholders[tile] = placeholder;
            tile.FirstFrameShown += RetirePlaceholder;
        }
    }

    void RetirePlaceholder(CameraTile tile)
    {
        tile.FirstFrameShown -= RetirePlaceholder;
        if (!_placeholders.Remove(tile, out var placeholder)) return;
        TileGrid.Children.Remove(placeholder);
        TrackShutdown(placeholder.ShutdownAsync(), placeholder.Camera.Name);
    }

    static void Position(CameraTile tile, TileSlot slot)
    {
        Grid.SetRow(tile, slot.Row);
        Grid.SetColumn(tile, slot.Column);
        Grid.SetRowSpan(tile, slot.RowSpan);
        Grid.SetColumnSpan(tile, slot.ColumnSpan);
        Panel.SetZIndex(tile, 0);
    }

    void DisposeTile((Guid Id, StreamKind Kind) key)
    {
        if (!_tiles.Remove(key, out var tile)) return;
        if (_placeholders.Remove(tile, out var placeholder))
        {
            TileGrid.Children.Remove(placeholder);
            TrackShutdown(placeholder.ShutdownAsync(), placeholder.Camera.Name);
        }
        TileGrid.Children.Remove(tile);
        TrackShutdown(tile.ShutdownAsync(), tile.Camera.Name);
    }

    void DisposeTilesOf(Guid cameraId)
    {
        foreach (var key in _tiles.Keys.Where(k => k.Id == cameraId).ToList()) DisposeTile(key);
    }

    void FeatureCamera(CameraTile tile)
    {
        if (_settings.LayoutMode != LayoutMode.Featured || tile.Kind == StreamKind.Main) return;
        _settings.FeaturedCameraId = tile.Camera.Id;
        SaveSettingsQuietly();
        RebuildView();
    }
}
```

  - `CreateTile(Camera camera, StreamKind kind)` now takes the kind and also subscribes `tile.Clicked += FeatureCamera;`.
  - Everywhere `DisposeTile(id)` was used for a camera (edit/delete), use `DisposeTilesOf(id)`.
  - `OnClosed` collects `_tiles.Values` and `_placeholders.Values`.
  - `SaveSettingsQuietly()` is the existing try/catch around `_settingsStore.Save(_settings)`, extracted into a method and reused by `OnClosing`.

- [ ] **Step 4: Combo mapping.** The combo selection sets `LayoutMode` and `GridMode`:

```csharp
    void GridMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GridModeBox.SelectedIndex == FeaturedIndex) _settings.LayoutMode = LayoutMode.Featured;
        else
        {
            _settings.LayoutMode = LayoutMode.Grid;
            _settings.GridMode = GridModes[GridModeBox.SelectedIndex];
        }
        SaveSettingsQuietly();
        RebuildView();
    }
```

In the constructor, set the initial index: `_settings.LayoutMode == LayoutMode.Featured ? FeaturedIndex : Math.Max(0, Array.IndexOf(GridModes, _settings.GridMode))`.

- [ ] **Step 5: Drop onto featured.** In `SwapCameras(source, target)`: if `_settings.LayoutMode == LayoutMode.Featured` and the target is the featured camera (the planner's `Slots[0].CameraId`), set `FeaturedCameraId = source`, save and rebuild. Otherwise swap `Order` as before. A deleted featured camera falls back automatically (ViewPlanner uses the first camera); clear `FeaturedCameraId` in `DeleteCamera` when it matches.
- [ ] **Step 6: Build, test and smoke.**
  - `dotnet build` → 0 warnings; `dotnet test CamaraWin.slnx` → green.
  - Smoke: seed three custom cameras (cam1, cam2, cam3 published by three ffmpeg processes), set `"layoutMode": "Featured"` in the seeded settings.json, launch, alive 8 s, close within 5 s, clean up.
- [ ] **Step 7: Commit** `feat(app): featured layout with click-to-feature and seamless stream swap` (with the trailer).

---

### Task 8: App — translated errors on tiles, toasts, error log window

**Files:**
- Create: `src/CamaraWin.App/ErrorCenter.cs`, `Toast.cs`, `ToastHost.xaml(.cs)`, `ErrorLogWindow.xaml(.cs)`, `MainWindow.Errors.cs` (partial)
- Modify: `CameraTile.xaml(.cs)` (translated status, Editar button, error/playing events), `MainWindow.xaml` (toast overlay, status-bar Registro button), `MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `StreamSession.ErrorOccurred` / `LastErrorKind` (Task 3); `ErrorTranslator`, `ErrorNotificationPolicy`, `ErrorLog`, `AppPaths.LogsDirectory` (Task 2); `RecordingController.ErrorOccurred` / `SessionStateChanged` (Task 6).
- Produces:
  - `sealed record Toast(string Title, string Message, bool IsRecovery)`.
  - `sealed class ErrorCenter(ErrorLog log)` with `event Action<Toast>? ToastRequested`, `event Action<int>? UnreadChanged`, `int Unread`, `void Report(Camera, StreamError)`, `void Playing(Camera)`, `void Forget(Guid)`, `void MarkRead()`.
  - `CameraTile` events `ErrorReported(CameraTile, StreamError)` and `PlayingReached(CameraTile)`.
  - `ToastHost.Show(Toast)` and `event Action? OpenLogRequested`.
  - `ErrorLogWindow(ErrorLog log)`.

- [ ] **Step 1: `Toast.cs` and `ErrorCenter.cs`:**

```csharp
namespace CamaraWin.App;

public sealed record Toast(string Title, string Message, bool IsRecovery);
```

```csharp
using CamaraWin.Core;

namespace CamaraWin.App;

/// <summary>Turns session errors into log entries and throttled toasts. UI thread only.</summary>
public sealed class ErrorCenter(ErrorLog log)
{
    readonly ErrorNotificationPolicy _policy = new();

    public event Action<Toast>? ToastRequested;
    public event Action<int>? UnreadChanged;
    public int Unread { get; private set; }

    public void Report(Camera camera, StreamError error)
    {
        var text = Translate(camera, error.Kind);
        log.Add(new ErrorLogEntry(DateTime.Now, camera.Name, text.Short, text.Title, error.Detail));
        Unread++;
        UnreadChanged?.Invoke(Unread);
        if (_policy.ShouldNotify(camera.Id, error.Kind)) ToastRequested?.Invoke(new Toast(text.Title, text.Advice, false));
    }

    public void Playing(Camera camera)
    {
        if (!_policy.OnPlaying(camera.Id)) return;
        log.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Recuperada", $"{camera.Name}: conexión recuperada", ""));
        ToastRequested?.Invoke(new Toast($"✓ {camera.Name}: conexión recuperada", "", true));
    }

    public void Forget(Guid cameraId) => _policy.Forget(cameraId);

    public void MarkRead()
    {
        Unread = 0;
        UnreadChanged?.Invoke(0);
    }

    /// <summary>An auth failure on a camera with no stored password means the password is missing (e.g. after importing a copy without passwords).</summary>
    public static TranslatedError Translate(Camera camera, StreamErrorKind kind) =>
        kind == StreamErrorKind.AuthFailed && camera.Password.Length == 0
            ? new TranslatedError("Falta contraseña", $"{camera.Name}: falta la contraseña", "Pulsa «Editar» en la cámara y escribe su contraseña.")
            : ErrorTranslator.Translate(kind, camera.Brand, camera.Name);
}
```

- [ ] **Step 2: Tile error display.**
  - In `CameraTile.xaml`, below `StatusLabel`, add a centered `Button x:Name="FixButton" Content="Editar" Padding="12,4" Margin="0,48,0,0" Visibility="Collapsed" Click="Edit_Click"`, and wrap StatusLabel and FixButton in a centered `StackPanel`.
  - In the constructor, subscribe:

```csharp
        _session.ErrorOccurred += error => Dispatcher.BeginInvoke(() => ShowError(error));
```

  - Add:

```csharp
    public event Action<CameraTile, StreamError>? ErrorReported;
    public event Action<CameraTile>? PlayingReached;
    StreamErrorKind? _errorKind;

    void ShowError(StreamError error)
    {
        if (_disposed) return;
        _errorKind = error.Kind;
        var text = ErrorCenter.Translate(Camera, error.Kind);
        StatusLabel.Text = error.Kind == StreamErrorKind.AuthFailed ? text.Short : $"{text.Short} · reintentando";
        FixButton.Visibility = error.Kind == StreamErrorKind.AuthFailed && _manage ? Visibility.Visible : Visibility.Collapsed;
        ErrorReported?.Invoke(this, error);
    }
```

  - In `ShowState`:
    - For `Reconnecting`/`Connecting` with `_errorKind` set, keep the translated label (do not overwrite it with "Reconectando…").
    - For `AuthFailed`, keep the translated label.
    - For `Playing`, clear `_errorKind`, hide FixButton and raise `PlayingReached?.Invoke(this)`.

- [ ] **Step 3: `ToastHost.xaml`:**

```xml
<UserControl x:Class="CamaraWin.App.ToastHost"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,16,16">
    <StackPanel x:Name="Stack" Width="360"/>
</UserControl>
```

`ToastHost.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CamaraWin.App;

public partial class ToastHost : UserControl
{
    const int MaxVisible = 4;

    public ToastHost() => InitializeComponent();

    public event Action? OpenLogRequested;

    public void Show(Toast toast)
    {
        var close = new Button { Content = "✕", Width = 24, Height = 24, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
        var text = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = toast.Title, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        if (toast.Message.Length > 0)
            text.Children.Add(new TextBlock { Text = toast.Message, Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var row = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(text);
        var card = new Border
        {
            Child = row, Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(toast.IsRecovery ? Color.FromRgb(0x1E, 0x4D, 0x2B) : Color.FromRgb(0x5A, 0x1F, 0x1F)),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(toast.IsRecovery ? 4 : 8) };
        void Remove() { timer.Stop(); Stack.Children.Remove(card); }
        timer.Tick += (_, _) => Remove();
        close.Click += (_, e) => { e.Handled = true; Remove(); };
        card.MouseLeftButtonUp += (_, _) => { Remove(); OpenLogRequested?.Invoke(); };
        Stack.Children.Add(card);
        while (Stack.Children.Count > MaxVisible) Stack.Children.RemoveAt(0);
        timer.Start();
    }
}
```

- [ ] **Step 4: `ErrorLogWindow.xaml(.cs)`.**
  - The window is 900x480, title "Registro de errores", with a `DataGrid x:Name="Grid" AutoGenerateColumns="False" IsReadOnly="True"`. Its columns are Hora (`Time`, format `dd/MM HH:mm:ss`), Cámara, Error (`Kind`), Título (`Title`), Detalle (`Detail`, star width).
  - Buttons: Copiar, Abrir carpeta, Limpiar.
  - Code-behind:

```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class ErrorLogWindow : Window
{
    readonly ErrorLog _log;
    readonly ObservableCollection<ErrorLogEntry> _rows;

    public ErrorLogWindow(ErrorLog log)
    {
        InitializeComponent();
        _log = log;
        _rows = new ObservableCollection<ErrorLogEntry>(log.Snapshot());
        Grid.ItemsSource = _rows;
        log.EntryAdded += OnEntryAdded;
        Closed += (_, _) => log.EntryAdded -= OnEntryAdded;
    }

    void OnEntryAdded(ErrorLogEntry entry) => Dispatcher.BeginInvoke(() => _rows.Insert(0, entry));

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var rows = Grid.SelectedItems.Count > 0 ? Grid.SelectedItems.Cast<ErrorLogEntry>() : _rows;
        Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(ErrorLog.FormatLine)));
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Process.Start("explorer.exe", $"\"{AppPaths.LogsDirectory}\"");
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        _rows.Clear();
    }
}
```

- [ ] **Step 5: MainWindow wiring (`MainWindow.Errors.cs`).**
  - `MainWindow.xaml`: wrap the view `Grid` content so that `<local:ToastHost x:Name="Toasts"/>` overlays it (same Grid, last child). In the status bar, add a right-aligned `Button x:Name="LogButton" Content="Registro" Click="OpenLog_Click"` (use `StatusBarItem` with `HorizontalAlignment="Right"` and a `DockPanel` inside the StatusBar).
  - Partial:

```csharp
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class MainWindow
{
    ErrorLog _errorLog = null!;
    ErrorCenter _errors = null!;
    ErrorLogWindow? _logWindow;

    void InitErrors()
    {
        ErrorLog.PurgeOlderThan(AppPaths.LogsDirectory, DateTime.Now);
        _errorLog = new ErrorLog(AppPaths.LogsDirectory);
        _errors = new ErrorCenter(_errorLog);
        _errors.ToastRequested += ShowToast;
        _errors.UnreadChanged += n => LogButton.Content = n == 0 ? "Registro" : $"Registro ({n})";
        Toasts.OpenLogRequested += OpenLog;
        _recordings.ErrorOccurred += (camera, error) => _errors.Report(camera, error);
        _recordings.SessionStateChanged += (camera, state) => { if (state == CamaraWin.Media.SessionState.Playing) _errors.Playing(camera); };
    }

    /// <summary>In-window toast; Task 10 routes this to the tray balloon while the window is hidden.</summary>
    void ShowToast(Toast toast) => Toasts.Show(toast);

    void OpenLog_Click(object sender, System.Windows.RoutedEventArgs e) => OpenLog();

    void OpenLog()
    {
        _errors.MarkRead();
        if (_logWindow is { IsLoaded: true }) { _logWindow.Activate(); return; }
        _logWindow = new ErrorLogWindow(_errorLog) { Owner = this };
        _logWindow.Show();
    }
}
```

  - Call `InitErrors()` in the constructor after `_recordings` is created.
  - In `CreateTile`, subscribe `tile.ErrorReported += (t, e) => _errors.Report(t.Camera, e);` and `tile.PlayingReached += t => _errors.Playing(t.Camera);`. Do the same for the fullscreen tile.
  - On delete, call `_errors.Forget(id)`.
  - In `OnClosed`, call `_errorLog.Dispose()` after the waits.
  - The Add dialog's test session and snapshot temp sessions are NOT wired (as in the spec).

- [ ] **Step 6: Build, test and smoke.**
  - `dotnet build` → 0 warnings; `dotnet test CamaraWin.slnx` → green.
  - Smoke: seed one camera pointing at the test server's `secure` path with a wrong password. Run mediamtx with the fixture-like config from a scratch folder, or use the default `tools/bin/mediamtx.yml` with an `authInternalUsers` entry added in the scratch copy. Launch, wait 8 s, and confirm that `%AppData%\CamaraWin\logs\camarawin-<today>.log` contains a line with `Contraseña incorrecta` and no password. Close and clean up.
- [ ] **Step 7: Commit** `feat(app): translated error states, toasts and error log window` (with the trailer).

---

### Task 9: App — menu bar, JSON import/export, automatic copy, manual/licence/support windows

**Files:**
- Create: `src/CamaraWin.App/MainWindow.Menu.cs`, `PassphraseDialog.xaml(.cs)`, `ExportDialog.xaml(.cs)`, `InfoWindow.xaml(.cs)`, `InfoDocuments.cs`
- Modify: `MainWindow.xaml` (Menu), `MainWindow.xaml.cs` (`SaveCameras` triggers the automatic copy; F11 also hides the menu)

**Interfaces:**
- Consumes: `CameraBackup`, `BackupMerge`, `BackupWriter`, `BackupFormatException`, `BackupPassphraseException`, `AppPaths.DefaultBackupDirectory`, `AppSettings.BackupFolder` (Tasks 1 and 5).
- Produces:
  - `static string? PassphraseDialog.Ask(Window owner, bool confirm)`.
  - `ExportDialog`, with `IncludePasswords` and `Passphrase` set after `ShowDialog() == true`.
  - `InfoWindow(string title, FlowDocument document)`.
  - `static class InfoDocuments { FlowDocument Manual(); FlowDocument License(); FlowDocument Support(); }`.
  - Menu handlers in the partial; `ExitApp()` (`Salir`) calls `Close()` for now (Task 10 turns the X into hide-to-tray).

- [ ] **Step 1: Menu XAML.** In `MainWindow.xaml`, add as the first DockPanel child:

```xml
        <Menu x:Name="MainMenu" DockPanel.Dock="Top">
            <MenuItem Header="_Archivo">
                <MenuItem Header="_Importar JSON…" Click="Import_Click"/>
                <MenuItem Header="_Exportar JSON…" Click="Export_Click"/>
                <MenuItem Header="_Carpeta de copia automática…" Click="BackupFolder_Click"/>
                <Separator/>
                <MenuItem Header="_Manual (README)" Click="Manual_Click"/>
                <MenuItem Header="_Licencia" Click="License_Click"/>
                <MenuItem Header="_Soporte" Click="Support_Click"/>
                <Separator/>
                <MenuItem Header="_Salir" Click="Exit_Click"/>
            </MenuItem>
        </Menu>
```

`ToggleFullscreen` also collapses and restores `MainMenu`.

- [ ] **Step 2: `PassphraseDialog`.**
  - The window is 360 wide with `SizeToContent=Height`, `WindowStartupLocation=CenterOwner` and `ResizeMode=NoResize`, titled "Clave de la copia".
  - It contains `PasswordBox First`, then `ConfirmPanel` (label "Repite la clave" + `PasswordBox Second`, collapsed unless `confirm`), `TextBlock Error` (red), and Aceptar (IsDefault) / Cancelar (IsCancel) buttons.

```csharp
public partial class PassphraseDialog : Window
{
    readonly bool _confirm;

    PassphraseDialog(bool confirm)
    {
        InitializeComponent();
        _confirm = confirm;
        ConfirmPanel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => First.Focus();
    }

    public static string? Ask(Window owner, bool confirm)
    {
        var dialog = new PassphraseDialog(confirm) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.First.Password : null;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_confirm && First.Password.Length < 8) { Error.Text = "La clave debe tener al menos 8 caracteres."; return; }
        if (_confirm && First.Password != Second.Password) { Error.Text = "Las claves no coinciden."; return; }
        if (First.Password.Length == 0) { Error.Text = "Escribe la clave."; return; }
        DialogResult = true;
    }
}
```

- [ ] **Step 3: `ExportDialog`.**
  - Controls: a CheckBox "Incluir contraseñas (cifradas con una clave)"; a panel with two PasswordBoxes (clave, repetir) enabled only when checked; a hint TextBlock ("Guarda la clave en un lugar seguro: sin ella no se pueden recuperar las contraseñas."); Error text; Exportar/Cancelar.
  - The same validation as PassphraseDialog confirm mode applies when checked.
  - Exposes `public bool IncludePasswords` and `public string? Passphrase`.

- [ ] **Step 4: `InfoWindow` and `InfoDocuments`.**
  - `InfoWindow.xaml`: 640x640 window with `FlowDocumentScrollViewer x:Name="Viewer"`. The code-behind sets `Title`, sets `Viewer.Document = document`, and handles `Hyperlink.RequestNavigate` via `AddHandler(Hyperlink.RequestNavigateEvent, …)`: `Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true;`.
  - `InfoDocuments.cs` builds the documents with small helpers `H(string)` (bold 18pt paragraph), `P(string)` and `Link(string text, string uri)`:
    - **Manual:** sections "Añadir cámaras" (Tapo: Cuenta de cámara; Imou: admin + código; Otra: URL RTSP; Buscar en red con ONVIF), "Vistas" (cuadrícula Auto/1/4/9/16, Principal + miniaturas con clic para cambiar, doble clic pantalla completa, F11, arrastrar para ordenar), "Grabar y capturas" (⏺ por cámara y Grabar todas, carpetas Vídeos\CamaraWin e Imágenes\CamaraWin, MKV reproducible en VLC), "Bandeja del sistema" (la X oculta, Salir cierra, Arrancar con Windows), "Copias de seguridad" (Exportar con/sin contraseñas, clave, copia automática sin contraseñas, Importar), "Errores frecuentes" (one paragraph per `StreamErrorKind` using `ErrorTranslator.Translate(kind, Brand.Custom, "La cámara")`: Short + Advice), "Estadísticas" (qué significan fps, ms y GPU/CPU; the real camera-to-screen delay cannot be measured because the camera clock is not synced).
    - **License:** plain-language summary ("Puedes usar, copiar, modificar, distribuir e incluso vender CamaraWin, siempre que mantengas el aviso de copyright y la licencia. Se ofrece «tal cual», sin garantías."), then the full MIT text (the same text as `LICENSE`, "Copyright (c) 2026 CamaraWin contributors"), then "FFmpeg" ("CamaraWin usa FFmpeg bajo licencia LGPL v2.1+ como bibliotecas separadas (carpeta ffmpeg); puedes sustituirlas por otra compilación compatible.") with the links `https://ffmpeg.org` and `https://github.com/BtbN/FFmpeg-Builds`.
    - **Support:** "¿Dudas, errores o sugerencias?" + `Link("www.tecxart.es", "https://www.tecxart.es")` + `Link("tecxart@gmail.com", "mailto:tecxart@gmail.com")` + "Incluye, si puedes, las líneas del Registro de errores (Registro › Copiar)."

- [ ] **Step 5: `MainWindow.Menu.cs`:**

```csharp
using System.IO;
using System.Windows;
using CamaraWin.Core;
using Microsoft.Win32;

namespace CamaraWin.App;

public partial class MainWindow
{
    string BackupFolder => string.IsNullOrWhiteSpace(_settings.BackupFolder) ? AppPaths.DefaultBackupDirectory : _settings.BackupFolder;

    void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Copia de CamaraWin (*.json)|*.json", InitialDirectory = BackupFolder };
        if (dialog.ShowDialog(this) != true) return;
        BackupImport imported;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            while (true)
            {
                try
                {
                    imported = CameraBackup.Import(json, () => PassphraseDialog.Ask(this, confirm: false));
                    break;
                }
                catch (BackupPassphraseException ex)
                {
                    if (MessageBox.Show(this, $"{ex.Message} ¿Probar con otra clave?", "Importar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is BackupFormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Importar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var merge = BackupMerge.Merge(_cameras, imported.Cameras);
        foreach (var id in merge.UpdatedIds)
        {
            if (_recordings.IsRecording(id)) _recordings.StopWithNotice(id);
            DisposeTilesOf(id);
        }
        _cameras.Clear();
        _cameras.AddRange(merge.Cameras);
        SaveCameras();
        RebuildView();
        MessageBox.Show(this, $"{merge.Added} añadidas, {merge.Updated} actualizadas, {merge.WithoutPassword} sin contraseña.",
            "Importar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var options = new ExportDialog { Owner = this };
        if (options.ShowDialog() != true) return;
        var save = new SaveFileDialog
        {
            Filter = "Copia de CamaraWin (*.json)|*.json",
            FileName = $"camarawin-camaras-{DateTime.Now:yyyy-MM-dd}.json",
            InitialDirectory = BackupFolder,
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(save.FileName, CameraBackup.Export(_cameras, options.IncludePasswords ? options.Passphrase : null));
            Notify($"Copia exportada: {save.FileName}", save.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Exportar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void BackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = BackupFolder, Title = "Carpeta de copia automática" };
        if (dialog.ShowDialog(this) != true) return;
        _settings.BackupFolder = dialog.FolderName;
        SaveSettingsQuietly();
        ScheduleAutomaticBackup();
    }

    /// <summary>Password-less copy after every camera-list change, off the UI thread.</summary>
    void ScheduleAutomaticBackup()
    {
        var snapshot = _cameras.Select(c => c.Clone()).ToList();
        var folder = BackupFolder;
        Task.Run(() => BackupWriter.WriteAutomatic(folder, snapshot)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (t.Exception is { } ex) Notify($"No se pudo guardar la copia automática: {ex.GetBaseException().Message}", null);
        }), TaskScheduler.Default);
    }

    void Manual_Click(object sender, RoutedEventArgs e) => new InfoWindow("Manual de CamaraWin", InfoDocuments.Manual()) { Owner = this }.Show();
    void License_Click(object sender, RoutedEventArgs e) => new InfoWindow("Licencia", InfoDocuments.License()) { Owner = this }.Show();
    void Support_Click(object sender, RoutedEventArgs e) => new InfoWindow("Soporte", InfoDocuments.Support()) { Owner = this }.Show();
    void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();

    void ExitApp() => Close();
}
```

  - `SaveCameras()` calls `ScheduleAutomaticBackup()` after a successful save.

- [ ] **Step 6: Build, test and smoke.**
  - `dotnet build` → 0 warnings; `dotnet test` → green.
  - Smoke: launch with seeded cameras and confirm `Documents\CamaraWin\camaras-copia.json` does NOT appear on launch; it appears only after a change. Instead, verify through UI Automation that the "Archivo" menu opens and lists its 7 items; close the app and clean up.
  - Export and import need file dialogs; the controller verifies them manually.
- [ ] **Step 7: Commit** `feat(app): menu with JSON import/export, automatic copy and info windows` (with the trailer).

---

### Task 10: App — system tray, single instance, start with Windows

**Files:**
- Create: `src/CamaraWin.App/TrayController.cs`, `TrayIcons.cs`, `SingleInstance.cs`, `MainWindow.Tray.cs`
- Modify: `CamaraWin.App.csproj`, `App.xaml.cs`, `MainWindow.xaml.cs` (constructor gains `bool startHidden`; the X hides; exit path)

**Interfaces:**
- Consumes: `AutoStart`, `RegistryRunKey` (Task 5), `RecordingController` (Task 6), `ErrorCenter.ToastRequested` (Task 8), `AppSettings.TrayHintShown`.
- Produces:
  - `sealed class TrayController : IDisposable` with events `OpenRequested`, `ExitRequested`, `RecordAllRequested` and `AutoStartToggled(bool)`, plus `SetRecording(bool any)`, `SetAutoStart(bool)` and `ShowBalloon(string title, string text)`.
  - `static class SingleInstance { bool TryClaim(); void ListenForActivation(Action onActivate); }`.
  - `MainWindow` gains `ShowFromTray()`, `HideToTray()`, `bool IsHiddenInTray`, and the constructor `MainWindow(bool startHidden)`.
  - `ExitApp()` performs the real exit.

- [ ] **Step 1: csproj.** Add inside the PropertyGroup: `<UseWindowsForms>true</UseWindowsForms>`. Add:

```xml
  <ItemGroup>
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
  </ItemGroup>
```

(Those two global usings would otherwise make `Application`, `MessageBox`, `Button` and similar types ambiguous with WPF.) Then build to confirm there are no ambiguities.

- [ ] **Step 2: `TrayIcons.cs`** (drawn at runtime, so the project needs no .ico file):

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CamaraWin.App;

static class TrayIcons
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(bool recording)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(0x33, 0x99, 0xFF));
            g.FillRectangle(body, 2, 9, 20, 15);
            g.FillPolygon(body, [new Point(22, 13), new Point(30, 9), new Point(30, 24), new Point(22, 20)]);
            using var lens = new SolidBrush(Color.White);
            g.FillEllipse(lens, 7, 12, 9, 9);
            if (recording)
            {
                using var dot = new SolidBrush(Color.Red);
                g.FillEllipse(dot, 18, 18, 13, 13);
            }
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
```

- [ ] **Step 3: `TrayController.cs`:**

```csharp
using System.Drawing;
using Forms = System.Windows.Forms;

namespace CamaraWin.App;

sealed class TrayController : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Icon _normal = TrayIcons.Create(recording: false);
    readonly Icon _recording = TrayIcons.Create(recording: true);
    readonly Forms.ToolStripMenuItem _recordAll;
    readonly Forms.ToolStripMenuItem _autoStart;

    public TrayController(bool autoStartEnabled)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => OpenRequested?.Invoke());
        _recordAll = new Forms.ToolStripMenuItem("Grabar todas", null, (_, _) => RecordAllRequested?.Invoke());
        menu.Items.Add(_recordAll);
        _autoStart = new Forms.ToolStripMenuItem("Arrancar con Windows") { CheckOnClick = true, Checked = autoStartEnabled };
        _autoStart.CheckedChanged += (_, _) => AutoStartToggled?.Invoke(_autoStart.Checked);
        menu.Items.Add(_autoStart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke());
        _icon = new Forms.NotifyIcon { Icon = _normal, Text = "CamaraWin", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? RecordAllRequested;
    public event Action<bool>? AutoStartToggled;

    public void SetRecording(bool any)
    {
        _icon.Icon = any ? _recording : _normal;
        _recordAll.Text = any ? "Detener todas" : "Grabar todas";
    }

    public void SetAutoStart(bool enabled) => _autoStart.Checked = enabled;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text.Length == 0 ? " " : text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _normal.Dispose();
        _recording.Dispose();
    }
}
```

- [ ] **Step 4: `SingleInstance.cs`:**

```csharp
namespace CamaraWin.App;

static class SingleInstance
{
    const string MutexName = @"Local\CamaraWin.SingleInstance";
    const string EventName = @"Local\CamaraWin.Activate";
    static Mutex? _mutex;
    static EventWaitHandle? _activate;

    /// <summary>True for the first instance; a later instance signals the first one and gets false.</summary>
    public static bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            return true;
        }
        try
        {
            using var existing = EventWaitHandle.OpenExisting(EventName);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // First instance is still starting; nothing to activate.
        }
        return false;
    }

    public static void ListenForActivation(Action onActivate)
    {
        var handle = _activate ?? throw new InvalidOperationException("Not the first instance.");
        new Thread(() =>
        {
            while (handle.WaitOne()) onActivate();
        }) { IsBackground = true, Name = "SingleInstance" }.Start();
    }
}
```

- [ ] **Step 5: App startup.** In `App.OnStartup`:
  - Before FFmpeg init: `if (!SingleInstance.TryClaim()) { Shutdown(0); return; }`.
  - Set `ShutdownMode = ShutdownMode.OnExplicitShutdown;`.
  - Create `var startHidden = e.Args.Contains("--tray");`, then `var window = new MainWindow(startHidden);`.
  - `if (!startHidden) window.Show();`
  - `SingleInstance.ListenForActivation(() => Dispatcher.BeginInvoke(window.ShowFromTray));`
  - Keep the existing try/catch around window creation.

- [ ] **Step 6: `MainWindow.Tray.cs`:**

```csharp
using System.Windows;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class MainWindow
{
    TrayController _tray = null!;
    AutoStart _autoStart = null!;
    bool _exitRequested;

    public bool IsHiddenInTray { get; private set; }

    void InitTray()
    {
        _autoStart = new AutoStart(new RegistryRunKey(), Environment.ProcessPath!);
        _tray = new TrayController(_autoStart.IsEnabled);
        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
        _tray.RecordAllRequested += ToggleRecordAll;
        _tray.AutoStartToggled += enabled =>
        {
            try { if (enabled) _autoStart.Enable(); else _autoStart.Disable(); }
            catch (Exception ex) { Notify($"No se pudo cambiar el arranque con Windows: {ex.Message}", null); _tray.SetAutoStart(_autoStart.IsEnabled); }
        };
        _recordings.StatusChanged += (_, _) => _tray.SetRecording(_recordings.AnyRecording);
    }

    public void HideToTray()
    {
        if (IsHiddenInTray) return;
        SaveWindowPlacement();
        foreach (var owned in OwnedWindows.Cast<Window>().ToList()) owned.Close();
        foreach (var key in _tiles.Keys.ToList()) DisposeTile(key);   // recordings keep running
        Hide();
        IsHiddenInTray = true;
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            SaveSettingsQuietly();
            _tray.ShowBalloon("CamaraWin sigue en la bandeja", "Las grabaciones continúan. Doble clic en el icono para abrirla; «Salir» la cierra.");
        }
    }

    public void ShowFromTray()
    {
        if (IsHiddenInTray)
        {
            IsHiddenInTray = false;
            Show();
            RebuildView();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // Replaces the Task 9 one-liner `void ExitApp() => Close();` in MainWindow.Menu.cs (delete that one).
    void ExitApp()
    {
        _exitRequested = true;
        if (IsHiddenInTray) { IsHiddenInTray = false; }
        Close();
    }
}
```

Changes to `MainWindow.xaml.cs`:
- Constructor signature `MainWindow(bool startHidden = false)`. Call `InitTray()` after `InitErrors()`. When `startHidden`, set `IsHiddenInTray = true` and do not call `RebuildView()`; otherwise call it.
- Extract the placement-saving part of `OnClosing` into `SaveWindowPlacement()`, which returns early when `!IsVisible`.
- `OnClosing`: `if (!_exitRequested) { e.Cancel = true; HideToTray(); return; }` goes first; the existing real-exit logic follows.
- `OnClosed`: after the existing waits, call `_tray.Dispose(); _errorLog.Dispose(); Application.Current.Shutdown();`.
- `ShowToast` (from Task 8): `if (IsHiddenInTray) _tray.ShowBalloon(toast.Title, toast.Message); else Toasts.Show(toast);`.
- `ToggleRecordAll()` is implemented in Task 11. For now, add it as `void ToggleRecordAll() { }` with the comment `// Task 11` so the build stays green; Task 11 replaces it.

- [ ] **Step 7: Build, test and smoke.**
  - `dotnet build` → 0 warnings; `dotnet test` → green.
  - Smoke:
    - (a) Launch; `CloseMainWindow()` → the process stays alive and the window is hidden (`MainWindowHandle` == 0 or not visible).
    - (b) Launch the exe a second time → the second process exits within 3 s and the first shows its window again.
    - (c) `taskkill` the first process only as the final cleanup step.
    - (d) Launch with `--tray` → alive, no visible window.
  - Clean up the seeded files and processes. Do NOT leave an autostart registry value: check `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` has no `CamaraWin` value that you added.
- [ ] **Step 8: Commit** `feat(app): system tray, single instance and start with Windows` (with the trailer).

---

### Task 11: App — Grabar todas and stats overlay (Ver menu)

**Files:**
- Modify: `MainWindow.xaml` (toolbar button, Ver menu), `MainWindow.xaml.cs` / `MainWindow.Tray.cs` (`ToggleRecordAll`), `CameraTile.xaml(.cs)` (stats label)

**Interfaces:**
- Consumes: `RecordingController` (Task 6), `StreamSession.Stats` (Task 4), `AppSettings.ShowStats` (Task 1), `ViewPlanner` (Task 1).
- Produces: `CameraTile.ShowStats { get; set; }`; `MainWindow.ToggleRecordAll()`.

- [ ] **Step 1: Toolbar and menu.**
  - After the Grabaciones button, add `<Button x:Name="RecordAllButton" Content="⏺ Grabar todas" Click="RecordAll_Click"/>`.
  - Add a second top-level menu:

```xml
            <MenuItem Header="_Ver">
                <MenuItem x:Name="ShowStatsItem" Header="Mostrar _estadísticas" IsCheckable="True" Click="ShowStats_Click"/>
            </MenuItem>
```

- [ ] **Step 2: Record all.**

```csharp
    void RecordAll_Click(object sender, RoutedEventArgs e) => ToggleRecordAll();

    void ToggleRecordAll()
    {
        if (_recordings.AnyRecording) { _recordings.StopAllWithNotice(); return; }
        IEnumerable<Guid> ids = IsHiddenInTray
            ? _cameras.Select(c => c.Id)
            : ViewPlanner.Plan(_cameras, _settings.LayoutMode, _settings.GridMode, _settings.FeaturedCameraId).Slots.Select(s => s.CameraId);
        foreach (var camera in _cameras.Where(c => ids.Contains(c.Id))) _recordings.Start(camera);
    }
```

Subscribe `_recordings.StatusChanged += (_, _) => RecordAllButton.Content = _recordings.AnyRecording ? "⏹ Detener todas" : "⏺ Grabar todas";`. Remove the Task 10 stub.

- [ ] **Step 3: Stats overlay in the tile.**
  - In `CameraTile.xaml`, add inside the Grid:

```xml
            <Border x:Name="StatsBorder" Background="#80000000" Padding="6,1" CornerRadius="0,4,0,0"
                    HorizontalAlignment="Left" VerticalAlignment="Bottom" Visibility="Collapsed">
                <TextBlock x:Name="StatsLabel" Foreground="White" FontSize="11" FontFamily="Consolas"/>
            </Border>
```

  - Code-behind:

```csharp
    DispatcherTimer? _statsTimer;

    public bool ShowStats
    {
        get => _statsTimer is not null;
        set
        {
            if (value == ShowStats || _session is null) return;
            if (value)
            {
                _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _statsTimer.Tick += (_, _) => RefreshStats();
                _statsTimer.Start();
                StatsBorder.Visibility = Visibility.Visible;
                RefreshStats();
            }
            else
            {
                _statsTimer!.Stop();
                _statsTimer = null;
                StatsBorder.Visibility = Visibility.Collapsed;
            }
        }
    }

    void RefreshStats()
    {
        if (_session is null) return;
        var s = _session.Stats;
        StatsLabel.Text = $"{s.Fps:0} fps · {s.LatencyMs:0} ms · {(s.HardwareDecoding ? "GPU" : "CPU")}";
        StatsLabel.Foreground = s.SinceLastFrame > TimeSpan.FromSeconds(1) ? Brushes.Orange : Brushes.White;
    }
```

  - In `ShutdownAsync`, stop `_statsTimer`. (Add `using System.Windows.Threading;`.)

- [ ] **Step 4: Ver menu wiring.**
  - Constructor: `ShowStatsItem.IsChecked = _settings.ShowStats;`.
  - `CreateTile` sets `tile.ShowStats = _settings.ShowStats;`, and so does the fullscreen tile.

```csharp
    void ShowStats_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowStats = ShowStatsItem.IsChecked;
        SaveSettingsQuietly();
        foreach (var tile in _tiles.Values) tile.ShowStats = _settings.ShowStats;
    }
```

- [ ] **Step 5: Build, test and smoke.**
  - `dotnet build` → 0 warnings; `dotnet test` → green.
  - Smoke: seed `"showStats": true` and one camera on the test stream; launch; alive 8 s; close via the tray exit path (`ExitApp` is reachable through UI Automation on the "Salir" menu item) and confirm the process exits within 5 s; clean up.
- [ ] **Step 6: Commit** `feat(app): record all cameras and per-tile stats overlay` (with the trailer).

---

### Task 12: Docs — README for v1.1 and acceptance

**Files:**
- Modify: `README.md`

- [ ] **Step 1: README.** Add feature bullets:
  - vista Principal + miniaturas
  - errores traducidos con avisos y Registro (`%AppData%\CamaraWin\logs`)
  - menú Archivo: importar/exportar JSON con clave opcional, copia automática en `Documentos\CamaraWin\camaras-copia.json`, formato en `docs/ejemplo-camaras.json`
  - bandeja del sistema, una sola instancia, Arrancar con Windows (`--tray`)
  - Grabar todas
  - Ver › Mostrar estadísticas (fps · ms · GPU/CPU; not the real camera-to-screen latency)
  - Soporte: https://www.tecxart.es · tecxart@gmail.com

  Keep every existing claim accurate. Remove unmeasured latency numbers or mark them as "objetivo".
- [ ] **Step 2: Full test run.** `dotnet test CamaraWin.slnx` → report the counts per project.
- [ ] **Step 3: Commit** `docs: README for v1.1` (with the trailer).
- [ ] **Step 4: Acceptance with the user (controller + user, not the implementer).** Check:
  - The featured layout with the 7 real cameras: click to swap, no black flash.
  - A wrong password on one Tapo and one Imou gives "Contraseña incorrecta", a toast and a log line.
  - Unplugging a camera gives "Sin conexión · reintentando", a toast after about 15 s, and "✓ recuperada" when plugged back in.
  - Export with a passphrase, delete a camera, import it back.
  - The automatic copy file is updated.
  - Tray: close, recordings continue, reopen, second launch activates the window, Arrancar con Windows toggles.
  - Grabar todas / Detener todas.
  - Stats show GPU on the user's machine.
