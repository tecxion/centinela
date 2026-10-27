# Centinela v1.3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Always-on motion detection (red frame, per-camera 👁 toggle, tray balloon when the window is not visible, separate motion log) and notification options (per camera, sounds, quiet hours).

**Architecture:** Pure logic in `Centinela.Core` (camera/setting fields, grayscale downscaler, pluggable `IMotionDetector` with a frame-difference implementation, `MotionTracker`, `NotificationGate`, a generic daily log reused by the error and motion logs). `Centinela.Media` gains `SharedStreams`: one shared substream `StreamSession` per camera handed out as leases, so a thumbnail and the detector use one connection. `Centinela.App` adds `MotionService` (background analysis at ~5 fps), tile UI (red frame, 👁), the motion log window and the notification options window, and routes every notice through the gate.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF (+WinForms tray), FFmpeg.AutoGen 9.0.1.1, NAudio.Wasapi 2.2.1 (unchanged), xUnit 2 + Xunit.SkippableFact, mediamtx for RTSP integration tests.

**Spec:** `docs/superpowers/specs/2026-09-28-v1.3-design.md`

## Global Constraints

- .NET 10, WPF, FFmpeg.AutoGen 9.0.1.1, NAudio.Wasapi 2.2.1. No new dependencies.
- Version: `<Version>1.3.0</Version>` in `Directory.Build.props`.
- User-visible text in Spanish; no translation system.
- Passwords never in plain text in logs, notices or UI.
- Tests never touch real data (`CENTINELA_DATA_DIR`), the Run key, or the network except the local mediamtx server.
- Nothing added may buffer or delay live video. Analysis never runs on the UI thread, nor on a video session thread beyond copying/downscaling one image inside `FrameMailbox.TryRead`.
- Compatibility: v1.2 `cameras.json`, `settings.json` and JSON copies load without errors (new fields get defaults).
- Detector numbers (spec): analysis image 160×90 gray; background EMA α = 0.05; pixel changed if |pixel − background| > 25; global change > 60 % → reset background, no motion; Low 3 % × 3 frames, Medium 1.5 % × 2, High 0.7 % × 2; event ends after 3 s without motion; default cooldown 60 s, allowed 30/60/300/900 s; detector lease 320×180; analysis ~5 fps (200 ms).
- Red frame `#E53935`, 3 px. Sounds: `SystemSounds.Exclamation` (connection lost), `SystemSounds.Asterisk` (motion).
- Motion log files `logs\movimiento-AAAA-MM-DD.log`, 500 in memory, purge after 14 days.
- Do not touch the user's running app or its data; build the App into a scratch output (`-p:BaseOutputPath=<scratch>\`) if `src/Centinela.App/bin` is locked.
- `git add` explicit paths only. Commit messages end with a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test commands: `dotnet test tests/Centinela.Core.Tests`, `dotnet test tests/Centinela.Media.Tests` (Media needs `tools/bin/mediamtx.exe` and ffmpeg with libx264/aac on PATH).

## File Structure

Core (`src/Centinela.Core`):
- `Camera.cs` — modify: motion/alert fields, `MotionSensitivity`.
- `CameraStore.cs` — modify: DTO fields.
- `Backup/CameraBackup.cs`, `Backup/BackupMerge.cs` — modify: fields in copies and merge.
- `Settings.cs` — modify: sounds, quiet hours.
- `Motion/GrayFrame.cs` — create: `GrayFrame`, `GrayScaler`.
- `Motion/MotionDetector.cs` — create: `IMotionDetector`, `MotionSample`, `FrameDiffDetector`.
- `Motion/MotionTracker.cs` — create.
- `Notifications/NotificationGate.cs` — create.
- `Logging/DailyLog.cs` — create; `Errors/ErrorLog.cs` — modify to use it; `Motion/MotionLog.cs` — create.

Media (`src/Centinela.Media`):
- `SharedStreams.cs` — create: shared session leases.

App (`src/Centinela.App`):
- `CameraTile.xaml(.cs)` — modify: shared lease for Sub, red frame, 👁.
- `MotionService.cs` — create; `MainWindow.Motion.cs` — create.
- `MotionLogWindow.xaml(.cs)` — create.
- `NotificationOptionsWindow.xaml(.cs)` — create.
- `ErrorCenter.cs`, `MainWindow.xaml(.cs)`, `MainWindow.View.cs`, `MainWindow.Errors.cs`, `MainWindow.Tray.cs`, `MainWindow.Menu.cs`, `InfoDocuments.cs` — modify.

Docs: `README.md`.

---

### Task 1: Camera and settings fields (Core)

**Files:**
- Modify: `Directory.Build.props`, `src/Centinela.Core/Camera.cs`, `src/Centinela.Core/CameraStore.cs`, `src/Centinela.Core/Backup/CameraBackup.cs`, `src/Centinela.Core/Backup/BackupMerge.cs`, `src/Centinela.Core/Settings.cs`
- Test: `tests/Centinela.Core.Tests/CameraStoreTests.cs`, `CameraBackupTests.cs`, `BackupMergeTests.cs`, `SettingsStoreTests.cs`

**Interfaces:**
- Produces: `enum MotionSensitivity { Low, Medium, High }`; `Camera.MotionEnabled = false`, `MotionSensitivity = Medium`, `MotionCooldownSeconds = 60`, `ConnectionAlerts = true`, `MotionAlerts = true`; `Camera.AllowedCooldowns = [30, 60, 300, 900]`; `Camera.NormalizeCooldown(int) : int`; `AppSettings.SoundOnConnectionLost = false`, `SoundOnMotion = false`, `QuietHoursEnabled = false`, `QuietFrom = "23:00"`, `QuietTo = "07:00"`; `Settings.TryParseTime(string?, out TimeSpan) : bool` (static, `HH:mm`).

- [ ] **Step 1: Failing tests**

Add to the existing test classes (reuse each class's temp-dir pattern):

```csharp
// CameraStoreTests
[Fact]
public void Motion_and_alert_fields_round_trip()
{
    var path = Path.Combine(_dir, "c.json");
    var store = new CameraStore(path);
    store.Save([new Camera { Name = "A", Host = "h", MotionEnabled = true, MotionSensitivity = MotionSensitivity.High,
        MotionCooldownSeconds = 300, ConnectionAlerts = false, MotionAlerts = false }]);
    var c = Assert.Single(store.Load());
    Assert.Equal((true, MotionSensitivity.High, 300, false, false),
        (c.MotionEnabled, c.MotionSensitivity, c.MotionCooldownSeconds, c.ConnectionAlerts, c.MotionAlerts));
}

[Fact]
public void V12_file_without_new_fields_gets_defaults_and_bad_values_are_repaired()
{
    Directory.CreateDirectory(_dir);
    var path = Path.Combine(_dir, "old.json");
    File.WriteAllText(path, """
        [ { "id": "6f1c6a36-4a6e-4a53-9a0f-1d0e0b0a0c01", "name": "Vieja", "brand": "Tapo", "host": "h", "port": 554, "user": "u", "order": 0 },
          { "id": "6f1c6a36-4a6e-4a53-9a0f-1d0e0b0a0c02", "name": "Rara", "brand": "Tapo", "host": "h2", "port": 554, "user": "u", "order": 1,
            "motionCooldownSeconds": 45, "motionSensitivity": 7 } ]
        """);
    var cams = new CameraStore(path).Load();
    Assert.Equal((false, MotionSensitivity.Medium, 60, true, true),
        (cams[0].MotionEnabled, cams[0].MotionSensitivity, cams[0].MotionCooldownSeconds, cams[0].ConnectionAlerts, cams[0].MotionAlerts));
    Assert.Equal((MotionSensitivity.Medium, 60), (cams[1].MotionSensitivity, cams[1].MotionCooldownSeconds));
}

// CameraBackupTests
[Fact]
public void Motion_and_alert_fields_are_exported_and_imported()
{
    var json = CameraBackup.Export([new Camera { Name = "A", Brand = Brand.Tapo, Host = "h", MotionEnabled = true,
        MotionSensitivity = MotionSensitivity.Low, MotionCooldownSeconds = 900, ConnectionAlerts = false, MotionAlerts = false }], null);
    var c = CameraBackup.Import(json, () => null).Cameras[0];
    Assert.Equal((true, MotionSensitivity.Low, 900, false, false),
        (c.MotionEnabled, c.MotionSensitivity, c.MotionCooldownSeconds, c.ConnectionAlerts, c.MotionAlerts));
}

[Fact]
public void Old_copies_without_motion_fields_import_with_defaults()
{
    var c = CameraBackup.Import("""{ "format": "centinela-cameras", "version": 1, "cameras": [ { "name": "A", "brand": "Tapo", "host": "h" } ] }""", () => null).Cameras[0];
    Assert.Equal((false, MotionSensitivity.Medium, 60, true, true),
        (c.MotionEnabled, c.MotionSensitivity, c.MotionCooldownSeconds, c.ConnectionAlerts, c.MotionAlerts));
}

// BackupMergeTests
[Fact]
public void Merge_copies_motion_fields_and_counts_them_as_changes()
{
    var existing = new Camera { Name = "A", Host = "h", Port = 554 };
    var incoming = existing.Clone();
    incoming.MotionEnabled = true;
    incoming.MotionAlerts = false;
    var result = BackupMerge.Merge([existing], [incoming]);
    Assert.Equal(1, result.Updated);
    Assert.True(result.Cameras[0].MotionEnabled);
    Assert.False(result.Cameras[0].MotionAlerts);
}

// SettingsStoreTests
[Fact]
public void Sound_and_quiet_defaults_and_invalid_times_are_repaired()
{
    Directory.CreateDirectory(_dir);
    var path = Path.Combine(_dir, "q.json");
    File.WriteAllText(path, """{ "quietHoursEnabled": true, "quietFrom": "25:00", "quietTo": "7" }""");
    var s = new SettingsStore(path).Load();
    Assert.True(s.QuietHoursEnabled);
    Assert.Equal(("23:00", "07:00"), (s.QuietFrom, s.QuietTo));
    Assert.False(new AppSettings().SoundOnConnectionLost);
    Assert.False(new AppSettings().SoundOnMotion);
}

[Theory]
[InlineData("00:00", true)] [InlineData("23:59", true)] [InlineData("7:05", false)] [InlineData("24:00", false)] [InlineData("", false)] [InlineData(null, false)]
public void TryParseTime_accepts_only_HH_mm(string? text, bool ok) => Assert.Equal(ok, AppSettings.TryParseTime(text, out _));
```

(If a class has no `_dir`, follow that class's existing temp pattern. `AppSettings.TryParseTime` is the static helper.)

- [ ] **Step 2: Run** `dotnet test tests/Centinela.Core.Tests` — expected: build errors.

- [ ] **Step 3: Implement**

`Directory.Build.props`: `<Version>1.3.0</Version>`.

`Camera.cs`:

```csharp
public enum MotionSensitivity { Low, Medium, High }
```

In `Camera`:

```csharp
    public bool MotionEnabled { get; set; }
    public MotionSensitivity MotionSensitivity { get; set; } = MotionSensitivity.Medium;
    /// <summary>Minimum time between two motion notices of this camera; one of <see cref="AllowedCooldowns"/>.</summary>
    public int MotionCooldownSeconds { get; set; } = 60;
    public bool ConnectionAlerts { get; set; } = true;
    public bool MotionAlerts { get; set; } = true;

    public static readonly int[] AllowedCooldowns = [30, 60, 300, 900];
    public static int NormalizeCooldown(int seconds) => AllowedCooldowns.Contains(seconds) ? seconds : 60;
```

`CameraStore.CameraDto`: add the five properties with the same defaults; `ToDto` copies them; `FromDto` copies with `MotionSensitivity = Enum.IsDefined(d.MotionSensitivity) ? d.MotionSensitivity : MotionSensitivity.Medium` and `MotionCooldownSeconds = Camera.NormalizeCooldown(d.MotionCooldownSeconds)`. A numeric out-of-range enum value (7) must deserialize (JsonStringEnumConverter accepts numbers) and then be repaired; if it throws for this converter, add `[JsonConverter]` leniency like `LenientBrandConverter` — the test decides.

`CameraBackup.BackupCamera`: add the five properties (defaults as above; `MotionSensitivity` lenient: unknown → Medium, reuse a small lenient converter). Export copies them; Import copies them normalized.

`BackupMerge`: copy the five fields from incoming to match; `Differs` compares them.

`Settings.cs` `AppSettings`:

```csharp
    public bool SoundOnConnectionLost { get; set; }
    public bool SoundOnMotion { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string QuietFrom { get; set; } = "23:00";
    public string QuietTo { get; set; } = "07:00";

    /// <summary>Exactly "HH:mm" (00:00–23:59).</summary>
    public static bool TryParseTime(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
```

`SettingsStore.Load` repair: `if (!AppSettings.TryParseTime(settings.QuietFrom, out _)) settings.QuietFrom = "23:00";` and same for `QuietTo` → `"07:00"`.

- [ ] **Step 4: Run** Core tests — all pass.

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props src/Centinela.Core/Camera.cs src/Centinela.Core/CameraStore.cs src/Centinela.Core/Backup/CameraBackup.cs src/Centinela.Core/Backup/BackupMerge.cs src/Centinela.Core/Settings.cs tests/Centinela.Core.Tests
git commit -m "feat(core): motion and alert options on cameras, sounds and quiet hours in settings"
```

---

### Task 2: Grayscale downscaler and frame-difference detector (Core)

**Files:**
- Create: `src/Centinela.Core/Motion/GrayFrame.cs`, `src/Centinela.Core/Motion/MotionDetector.cs`
- Test: `tests/Centinela.Core.Tests/GrayScalerTests.cs`, `tests/Centinela.Core.Tests/FrameDiffDetectorTests.cs`

**Interfaces:**
- Produces: `sealed record GrayFrame(int Width, int Height, byte[] Pixels)`; `static class GrayScaler { const int Width = 160, Height = 90; static void FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride, GrayFrame target); static GrayFrame Create() }`; `readonly record struct MotionSample(bool Motion, double ChangedFraction)`; `interface IMotionDetector { MotionSample Analyze(GrayFrame frame); void Reset(); }`; `sealed class FrameDiffDetector(MotionSensitivity sensitivity) : IMotionDetector`.

- [ ] **Step 1: Failing tests**

`GrayScalerTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class GrayScalerTests
{
    static byte[] Bgra(int w, int h, Func<int, int, (byte B, byte G, byte R)> color)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (b, g, r) = color(x, y);
                var i = (y * w + x) * 4;
                (data[i], data[i + 1], data[i + 2], data[i + 3]) = (b, g, r, 255);
            }
        return data;
    }

    [Fact]
    public void Output_is_160x90()
    {
        var frame = GrayScaler.Create();
        Assert.Equal((160, 90, 160 * 90), (frame.Width, frame.Height, frame.Pixels.Length));
    }

    [Fact]
    public void Solid_color_becomes_its_luma()
    {
        var frame = GrayScaler.Create();
        GrayScaler.FromBgra(Bgra(320, 180, (_, _) => (0, 0, 255)), 320, 180, 320 * 4, frame); // pure red
        Assert.All(frame.Pixels, p => Assert.InRange(p, 74, 78)); // ≈ 0.299·255
    }

    [Fact]
    public void Left_black_right_white_keeps_its_halves()
    {
        var frame = GrayScaler.Create();
        GrayScaler.FromBgra(Bgra(640, 360, (x, _) => x < 320 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255)), 640, 360, 640 * 4, frame);
        Assert.Equal(0, frame.Pixels[10 * 160 + 10]);
        Assert.InRange(frame.Pixels[10 * 160 + 150], 250, 255);
    }

    [Fact]
    public void Works_with_a_padded_stride_and_small_sources()
    {
        var frame = GrayScaler.Create();
        var src = new byte[100 * 4 * 50 + 64];
        GrayScaler.FromBgra(src, 90, 50, 100 * 4, frame); // stride wider than width·4, source smaller than 160×90
        Assert.All(frame.Pixels, p => Assert.Equal(0, p));
    }
}
```

`FrameDiffDetectorTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class FrameDiffDetectorTests
{
    static GrayFrame Solid(byte value)
    {
        var f = GrayScaler.Create();
        Array.Fill(f.Pixels, value);
        return f;
    }

    /// <summary>Background 50 with a 30×30 square of 200 whose left edge is at <paramref name="x"/>.</summary>
    static GrayFrame Square(int x)
    {
        var f = Solid(50);
        for (var yy = 30; yy < 60; yy++)
            for (var xx = x; xx < x + 30; xx++)
                f.Pixels[yy * 160 + xx] = 200;
        return f;
    }

    static int FirstMotion(IMotionDetector d, IEnumerable<GrayFrame> frames)
    {
        var i = 0;
        foreach (var f in frames)
        {
            if (d.Analyze(f).Motion) return i;
            i++;
        }
        return -1;
    }

    static IEnumerable<GrayFrame> Moving(int count) => Enumerable.Range(0, count).Select(i => Square(5 + i * 15));

    [Theory]
    [InlineData(MotionSensitivity.Low, 3)]
    [InlineData(MotionSensitivity.Medium, 2)]
    [InlineData(MotionSensitivity.High, 2)]
    public void Moving_square_is_detected_after_N_changed_frames(MotionSensitivity sensitivity, int expectedIndex) =>
        Assert.Equal(expectedIndex, FirstMotion(new FrameDiffDetector(sensitivity), Moving(8)));

    [Fact]
    public void First_frame_never_detects()
    {
        var sample = new FrameDiffDetector(MotionSensitivity.High).Analyze(Square(5));
        Assert.Equal((false, 0.0), (sample.Motion, sample.ChangedFraction));
    }

    [Fact]
    public void Static_scene_never_detects() =>
        Assert.Equal(-1, FirstMotion(new FrameDiffDetector(MotionSensitivity.High), Enumerable.Repeat(0, 30).Select(_ => Square(40))));

    [Fact]
    public void Sensor_noise_never_detects()
    {
        var random = new Random(7);
        var frames = Enumerable.Range(0, 40).Select(_ =>
        {
            var f = GrayScaler.Create();
            for (var i = 0; i < f.Pixels.Length; i++) f.Pixels[i] = (byte)(100 + random.Next(-10, 11));
            return f;
        });
        Assert.Equal(-1, FirstMotion(new FrameDiffDetector(MotionSensitivity.High), frames));
    }

    [Fact]
    public void Global_light_change_is_ignored_and_resets_the_background()
    {
        var d = new FrameDiffDetector(MotionSensitivity.High);
        d.Analyze(Solid(40));
        var flash = d.Analyze(Solid(200));
        Assert.False(flash.Motion);
        Assert.True(flash.ChangedFraction > 0.6);
        Assert.Equal(-1, FirstMotion(d, Enumerable.Repeat(0, 10).Select(_ => Solid(200))));
    }

    [Fact]
    public void Reset_starts_over()
    {
        var d = new FrameDiffDetector(MotionSensitivity.Medium);
        foreach (var f in Moving(3)) d.Analyze(f);
        d.Reset();
        Assert.False(d.Analyze(Square(80)).Motion);
    }
}
```

- [ ] **Step 2: Run** — build errors.

- [ ] **Step 3: Implement**

`Motion/GrayFrame.cs`:

```csharp
namespace Centinela.Core;

/// <summary>An 8-bit luminance image, row-major, no padding.</summary>
public sealed record GrayFrame(int Width, int Height, byte[] Pixels);

public static class GrayScaler
{
    public const int Width = 160, Height = 90;

    public static GrayFrame Create() => new(Width, Height, new byte[Width * Height]);

    /// <summary>
    /// Downscales a BGRA image into <paramref name="target"/> (160×90) by sampling the centre of each block
    /// (integer luma ≈ 0.299 R + 0.587 G + 0.114 B). Cheap enough to run inside the frame mailbox lock.
    /// </summary>
    public static void FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride, GrayFrame target)
    {
        var pixels = target.Pixels;
        for (var ty = 0; ty < target.Height; ty++)
        {
            var sy = Math.Min(height - 1, (int)((ty + 0.5) * height / target.Height));
            var row = sy * stride;
            for (var tx = 0; tx < target.Width; tx++)
            {
                var sx = Math.Min(width - 1, (int)((tx + 0.5) * width / target.Width));
                var i = row + sx * 4;
                pixels[ty * target.Width + tx] = (byte)((29 * bgra[i] + 150 * bgra[i + 1] + 77 * bgra[i + 2]) >> 8);
            }
        }
    }
}
```

`Motion/MotionDetector.cs`:

```csharp
namespace Centinela.Core;

public readonly record struct MotionSample(bool Motion, double ChangedFraction);

/// <summary>Decides from successive small gray frames whether something moves. Implementations are per camera, not thread-safe.</summary>
public interface IMotionDetector
{
    MotionSample Analyze(GrayFrame frame);
    void Reset();
}

/// <summary>
/// Frame difference against a slowly adapting background: a pixel changed if it differs by more than 25;
/// motion when enough pixels changed for N frames in a row. A sudden change of most of the image (lights,
/// reconnection) resets the background instead of reporting motion.
/// </summary>
public sealed class FrameDiffDetector(MotionSensitivity sensitivity) : IMotionDetector
{
    const float Alpha = 0.05f;
    const int PixelThreshold = 25;
    const double GlobalChange = 0.60;

    readonly (double Fraction, int Frames) _trigger = sensitivity switch
    {
        MotionSensitivity.Low => (0.03, 3),
        MotionSensitivity.High => (0.007, 2),
        _ => (0.015, 2),
    };
    float[]? _background;
    int _consecutive;

    public MotionSample Analyze(GrayFrame frame)
    {
        var pixels = frame.Pixels;
        if (_background is null || _background.Length != pixels.Length)
        {
            Initialize(pixels);
            return new MotionSample(false, 0);
        }
        var changed = 0;
        for (var i = 0; i < pixels.Length; i++)
            if (Math.Abs(pixels[i] - _background[i]) > PixelThreshold) changed++;
        var fraction = changed / (double)pixels.Length;
        if (fraction > GlobalChange)
        {
            Initialize(pixels);
            return new MotionSample(false, fraction);
        }
        for (var i = 0; i < pixels.Length; i++) _background[i] += Alpha * (pixels[i] - _background[i]);
        _consecutive = fraction >= _trigger.Fraction ? _consecutive + 1 : 0;
        return new MotionSample(_consecutive >= _trigger.Frames, fraction);
    }

    public void Reset()
    {
        _background = null;
        _consecutive = 0;
    }

    void Initialize(byte[] pixels)
    {
        _background ??= new float[pixels.Length];
        if (_background.Length != pixels.Length) _background = new float[pixels.Length];
        for (var i = 0; i < pixels.Length; i++) _background[i] = pixels[i];
        _consecutive = 0;
    }
}
```

(Moving square check: 30×30 moved 15 px → 900 changed pixels = 6.25 % per frame from frame index 1, so Low hits at index 3, Medium/High at index 2.)

- [ ] **Step 4: Run** Core tests — pass. **Step 5: Commit**

```bash
git add src/Centinela.Core/Motion tests/Centinela.Core.Tests/GrayScalerTests.cs tests/Centinela.Core.Tests/FrameDiffDetectorTests.cs
git commit -m "feat(core): grayscale downscaler and frame-difference motion detector"
```

---

### Task 3: Motion tracker and notification gate (Core)

**Files:**
- Create: `src/Centinela.Core/Motion/MotionTracker.cs`, `src/Centinela.Core/Notifications/NotificationGate.cs`
- Test: `tests/Centinela.Core.Tests/MotionTrackerTests.cs`, `tests/Centinela.Core.Tests/NotificationGateTests.cs`

**Interfaces:**
- Produces: `enum MotionTransition { None, Started, Ended }`; `sealed record MotionEvent(DateTimeOffset Start, DateTimeOffset End, double Peak)`; `sealed record MotionUpdate(MotionTransition Transition, bool Alert, MotionEvent? Event)` with `static MotionUpdate None`; `sealed class MotionTracker(TimeSpan cooldown)` with `Cooldown { get; set; }`, `IsActive`, `Update(MotionSample, DateTimeOffset) : MotionUpdate`, `Flush(DateTimeOffset) : MotionUpdate`, `static readonly TimeSpan EndAfter = 3 s`; `enum NoticeKind { ConnectionLost, ConnectionRecovered, Motion }`; `readonly record struct NoticeDecision(bool Show, bool PlaySound)`; `static class NotificationGate { Decide(NoticeKind, Camera, AppSettings, bool windowVisible, DateTime now) ; IsQuiet(AppSettings, TimeSpan timeOfDay) }`.

- [ ] **Step 1: Failing tests**

`MotionTrackerTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class MotionTrackerTests
{
    static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    static MotionSample Yes(double f = 0.05) => new(true, f);
    static readonly MotionSample No = new(false, 0);

    [Fact]
    public void Starts_with_alert_then_ends_after_3_s_without_motion_with_peak()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        var start = t.Update(Yes(0.02), T0);
        Assert.Equal((MotionTransition.Started, true), (start.Transition, start.Alert));
        Assert.True(t.IsActive);
        Assert.Equal(MotionTransition.None, t.Update(Yes(0.09), T0.AddSeconds(1)).Transition);
        Assert.Equal(MotionTransition.None, t.Update(No, T0.AddSeconds(3.5)).Transition);
        var end = t.Update(No, T0.AddSeconds(4));
        Assert.Equal(MotionTransition.Ended, end.Transition);
        Assert.Equal(new MotionEvent(T0, T0.AddSeconds(1), 0.09), end.Event);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void Second_event_within_cooldown_starts_without_alert()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        Assert.True(t.Update(Yes(), T0).Alert);
        t.Update(No, T0.AddSeconds(5));
        var again = t.Update(Yes(), T0.AddSeconds(30));
        Assert.Equal((MotionTransition.Started, false), (again.Transition, again.Alert));
        t.Update(No, T0.AddSeconds(40));
        Assert.True(t.Update(Yes(), T0.AddSeconds(61)).Alert);
    }

    [Fact]
    public void Cooldown_can_change_at_runtime()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(900));
        t.Update(Yes(), T0);
        t.Update(No, T0.AddSeconds(5));
        t.Cooldown = TimeSpan.FromSeconds(30);
        Assert.True(t.Update(Yes(), T0.AddSeconds(31)).Alert);
    }

    [Fact]
    public void Flush_ends_an_active_event_and_is_a_no_op_when_idle()
    {
        var t = new MotionTracker(TimeSpan.FromSeconds(60));
        Assert.Equal(MotionTransition.None, t.Flush(T0).Transition);
        t.Update(Yes(0.04), T0);
        var flushed = t.Flush(T0.AddSeconds(2));
        Assert.Equal(MotionTransition.Ended, flushed.Transition);
        Assert.Equal(0.04, flushed.Event!.Peak);
    }
}
```

`NotificationGateTests.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Core.Tests;

public class NotificationGateTests
{
    static readonly DateTime Noon = new(2026, 9, 28, 12, 0, 0);
    static readonly Camera Cam = new() { Name = "A" };

    [Fact]
    public void Connection_lost_shows_and_sounds_only_if_enabled()
    {
        Assert.Equal(new NoticeDecision(true, false), NotificationGate.Decide(NoticeKind.ConnectionLost, Cam, new AppSettings(), true, Noon));
        Assert.Equal(new NoticeDecision(true, true), NotificationGate.Decide(NoticeKind.ConnectionLost, Cam, new AppSettings { SoundOnConnectionLost = true }, true, Noon));
        Assert.Equal(new NoticeDecision(false, false), NotificationGate.Decide(NoticeKind.ConnectionLost, new Camera { ConnectionAlerts = false }, new AppSettings { SoundOnConnectionLost = true }, true, Noon));
    }

    [Fact]
    public void Recovery_never_sounds() =>
        Assert.Equal(new NoticeDecision(true, false), NotificationGate.Decide(NoticeKind.ConnectionRecovered, Cam, new AppSettings { SoundOnConnectionLost = true }, false, Noon));

    [Fact]
    public void Motion_only_when_the_window_is_not_visible()
    {
        var s = new AppSettings { SoundOnMotion = true };
        Assert.Equal(new NoticeDecision(false, false), NotificationGate.Decide(NoticeKind.Motion, Cam, s, windowVisible: true, Noon));
        Assert.Equal(new NoticeDecision(true, true), NotificationGate.Decide(NoticeKind.Motion, Cam, s, windowVisible: false, Noon));
        Assert.Equal(new NoticeDecision(false, false), NotificationGate.Decide(NoticeKind.Motion, new Camera { MotionAlerts = false }, s, false, Noon));
    }

    [Theory]
    [InlineData("23:00", "07:00", "23:30", true)]
    [InlineData("23:00", "07:00", "03:00", true)]
    [InlineData("23:00", "07:00", "07:00", false)]
    [InlineData("23:00", "07:00", "22:59", false)]
    [InlineData("13:00", "15:00", "13:00", true)]
    [InlineData("13:00", "15:00", "15:00", false)]
    [InlineData("08:00", "08:00", "08:00", false)]
    public void Quiet_hours(string from, string to, string at, bool quiet) =>
        Assert.Equal(quiet, NotificationGate.IsQuiet(new AppSettings { QuietHoursEnabled = true, QuietFrom = from, QuietTo = to }, TimeSpan.Parse(at)));

    [Fact]
    public void Quiet_hours_silence_everything_and_can_be_disabled()
    {
        var s = new AppSettings { QuietHoursEnabled = true, QuietFrom = "11:00", QuietTo = "13:00", SoundOnConnectionLost = true, SoundOnMotion = true };
        Assert.Equal(default, NotificationGate.Decide(NoticeKind.ConnectionLost, Cam, s, false, Noon));
        Assert.Equal(default, NotificationGate.Decide(NoticeKind.Motion, Cam, s, false, Noon));
        s.QuietHoursEnabled = false;
        Assert.True(NotificationGate.Decide(NoticeKind.Motion, Cam, s, false, Noon).Show);
    }
}
```

- [ ] **Step 2: Run** — build errors.

- [ ] **Step 3: Implement**

`Motion/MotionTracker.cs`:

```csharp
namespace Centinela.Core;

public enum MotionTransition { None, Started, Ended }

public sealed record MotionEvent(DateTimeOffset Start, DateTimeOffset End, double Peak);

public sealed record MotionUpdate(MotionTransition Transition, bool Alert, MotionEvent? Event)
{
    public static readonly MotionUpdate None = new(MotionTransition.None, false, null);
}

/// <summary>
/// Turns per-frame samples of one camera into events: starts on the first motion sample, ends after
/// <see cref="EndAfter"/> without motion. Alerts only on start, at most once per <see cref="Cooldown"/>.
/// </summary>
public sealed class MotionTracker(TimeSpan cooldown)
{
    public static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(3);

    DateTimeOffset _start, _lastMotion;
    DateTimeOffset? _lastAlert;
    double _peak;

    public TimeSpan Cooldown { get; set; } = cooldown;
    public bool IsActive { get; private set; }

    public MotionUpdate Update(MotionSample sample, DateTimeOffset now)
    {
        if (sample.Motion)
        {
            if (IsActive)
            {
                _lastMotion = now;
                _peak = Math.Max(_peak, sample.ChangedFraction);
                return MotionUpdate.None;
            }
            IsActive = true;
            _start = _lastMotion = now;
            _peak = sample.ChangedFraction;
            var alert = _lastAlert is not { } last || now - last >= Cooldown;
            if (alert) _lastAlert = now;
            return new MotionUpdate(MotionTransition.Started, alert, null);
        }
        return IsActive && now - _lastMotion >= EndAfter ? End() : MotionUpdate.None;
    }

    /// <summary>Ends an active event now (detection turned off, camera removed, app closing).</summary>
    public MotionUpdate Flush(DateTimeOffset now) => IsActive ? End() : MotionUpdate.None;

    MotionUpdate End()
    {
        IsActive = false;
        return new MotionUpdate(MotionTransition.Ended, false, new MotionEvent(_start, _lastMotion, _peak));
    }
}
```

(`Flush`'s `now` is unused by design — the event ends at the last motion seen; keep the parameter for callers' clarity or drop it and update the test; implementer's choice, keep tests meaningful.)

`Notifications/NotificationGate.cs`:

```csharp
namespace Centinela.Core;

public enum NoticeKind { ConnectionLost, ConnectionRecovered, Motion }

public readonly record struct NoticeDecision(bool Show, bool PlaySound);

/// <summary>
/// Whether a notice is shown and a sound played. Logs and the red frame never pass through here.
/// Motion notices are only for a window that is not visible (minimized or in the tray).
/// </summary>
public static class NotificationGate
{
    public static NoticeDecision Decide(NoticeKind kind, Camera camera, AppSettings settings, bool windowVisible, DateTime now)
    {
        if (IsQuiet(settings, now.TimeOfDay)) return default;
        return kind switch
        {
            NoticeKind.ConnectionLost when camera.ConnectionAlerts => new(true, settings.SoundOnConnectionLost),
            NoticeKind.ConnectionRecovered when camera.ConnectionAlerts => new(true, false),
            NoticeKind.Motion when camera.MotionAlerts && !windowVisible => new(true, settings.SoundOnMotion),
            _ => default,
        };
    }

    /// <summary>[From, To) local time; crosses midnight when From &gt; To; equal times mean no quiet hours.</summary>
    public static bool IsQuiet(AppSettings settings, TimeSpan timeOfDay)
    {
        if (!settings.QuietHoursEnabled
            || !AppSettings.TryParseTime(settings.QuietFrom, out var from)
            || !AppSettings.TryParseTime(settings.QuietTo, out var to)
            || from == to) return false;
        return from < to ? timeOfDay >= from && timeOfDay < to : timeOfDay >= from || timeOfDay < to;
    }
}
```

- [ ] **Step 4: Run** Core tests — pass. **Step 5: Commit**

```bash
git add src/Centinela.Core/Motion/MotionTracker.cs src/Centinela.Core/Notifications tests/Centinela.Core.Tests/MotionTrackerTests.cs tests/Centinela.Core.Tests/NotificationGateTests.cs
git commit -m "feat(core): motion event tracker and notification gate with quiet hours"
```

---

### Task 4: Daily log shared by the error and motion logs (Core)

**Files:**
- Create: `src/Centinela.Core/Logging/DailyLog.cs`, `src/Centinela.Core/Motion/MotionLog.cs`
- Modify: `src/Centinela.Core/Errors/ErrorLog.cs`
- Test: `tests/Centinela.Core.Tests/ErrorLogTests.cs` (must keep passing unchanged), `tests/Centinela.Core.Tests/MotionLogTests.cs` (create)

**Interfaces:**
- Produces: `sealed class DailyLog<T> : IDisposable where T : class` — ctor `(string directory, string prefix, Func<T, DateTime> time, Func<T, string> format, int capacity = 500)`; `Add(T)`, `Snapshot() : IReadOnlyList<T>` (newest first), `Clear()`, `event Action<T>? EntryAdded`, `Dispose()`, `static string FileFor(string directory, string prefix, DateTime time)`, `static int PurgeOlderThan(string directory, string prefix, DateTime now, int days = 14)`.
- `ErrorLog` keeps its exact public API (`Capacity`, `Add`, `Snapshot`, `Clear`, `EntryAdded`, `FileFor(dir, time)`, `FormatLine`, `PurgeOlderThan(dir, now, days)`, `Dispose`) implemented over `DailyLog<ErrorLogEntry>` with prefix `"centinela-"`.
- `sealed record MotionLogEntry(DateTime Start, string Camera, TimeSpan Duration, double Peak)`; `sealed class MotionLog : IDisposable` with the same API shape as `ErrorLog` over `DailyLog<MotionLogEntry>` with prefix `"movimiento-"`: `Add`, `Snapshot`, `Clear`, `EntryAdded`, `FileFor(dir, time)`, `FormatLine(entry)`, `PurgeOlderThan(dir, now, days = 14)`, `Dispose`.
- `MotionLog.FormatLine`: `yyyy-MM-dd HH:mm:ss<TAB>camera<TAB>duration<TAB>peak` — duration `m:ss` under an hour (`0:07`, `12:30`), `h:mm:ss` from an hour (`1:02:03`); peak as percent with one decimal and invariant culture (`4.2 %`); tabs/newlines in the camera name replaced by spaces.

- [ ] **Step 1: Failing tests** `MotionLogTests.cs` (mirror `ErrorLogTests` structure: temp dir, dispose):

```csharp
[Fact]
public void Format_line()
{
    Assert.Equal("2026-09-28 10:00:05\tGar aje\t0:07\t4.2 %",
        MotionLog.FormatLine(new MotionLogEntry(new DateTime(2026, 9, 28, 10, 0, 5), "Gar\taje", TimeSpan.FromSeconds(7), 0.0421)));
    Assert.Equal("2026-09-28 10:00:05\tA\t1:02:03\t60.0 %",
        MotionLog.FormatLine(new MotionLogEntry(new DateTime(2026, 9, 28, 10, 0, 5), "A", new TimeSpan(1, 2, 3), 0.6)));
}

[Fact]
public void Writes_daily_file_with_its_own_prefix_and_keeps_newest_first()
{
    var t = new DateTime(2026, 9, 28, 10, 0, 0);
    using (var log = new MotionLog(_dir))
    {
        log.Add(new MotionLogEntry(t, "A", TimeSpan.FromSeconds(3), 0.01));
        log.Add(new MotionLogEntry(t.AddMinutes(1), "B", TimeSpan.FromSeconds(4), 0.02));
        Assert.Equal("B", log.Snapshot()[0].Camera);
    }
    Assert.EndsWith("movimiento-2026-09-28.log", MotionLog.FileFor(_dir, t));
    Assert.Equal(2, File.ReadAllLines(MotionLog.FileFor(_dir, t)).Length);
}

[Fact]
public void Purge_only_touches_motion_files()
{
    Directory.CreateDirectory(_dir);
    var now = new DateTime(2026, 9, 28);
    File.WriteAllText(MotionLog.FileFor(_dir, now.AddDays(-20)), "old");
    File.WriteAllText(ErrorLog.FileFor(_dir, now.AddDays(-20)), "old error");
    Assert.Equal(1, MotionLog.PurgeOlderThan(_dir, now));
    Assert.True(File.Exists(ErrorLog.FileFor(_dir, now.AddDays(-20))));
}
```

- [ ] **Step 2: Run** — build errors.

- [ ] **Step 3: Implement** — move the ring buffer + background writer + purge logic from `ErrorLog` into `DailyLog<T>` (same behaviour: capacity ring, `BlockingCollection` writer task, UTF-8 append, swallow write errors, 2 s bounded dispose, `EntryAdded` after enqueue, tolerate `Add` after dispose). `ErrorLog` and `MotionLog` become thin wrappers (composition) exposing their existing/new static helpers. No duplicated logic between the two logs.

- [ ] **Step 4: Run** Core tests — all pass, including the untouched `ErrorLogTests`. **Step 5: Commit**

```bash
git add src/Centinela.Core/Logging src/Centinela.Core/Motion/MotionLog.cs src/Centinela.Core/Errors/ErrorLog.cs tests/Centinela.Core.Tests/MotionLogTests.cs
git commit -m "feat(core): generic daily log; motion log beside the error log"
```

---

### Task 5: Shared substreams (Media)

**Files:**
- Create: `src/Centinela.Media/SharedStreams.cs`
- Test: `tests/Centinela.Media.Tests/SharedStreamsTests.cs`

**Interfaces:**
- Consumes: `StreamSession`, `FrameMailbox` (multi-reader: each reader keeps its own sequence), `GrayScaler`, `FrameDiffDetector` (Task 2).
- Produces:

```csharp
public sealed class SharedStreams : IDisposable
{
    /// <summary>
    /// A lease on the session for (key, url, useUdp). The first lease creates it, calls onCreated (to wire
    /// events once) and starts it; the last release stops it off the calling thread.
    /// </summary>
    public SharedStreamLease Acquire(Guid key, string url, bool useUdp, int width, int height, Action<StreamSession>? onCreated = null);
    public int Count { get; }   // live sessions (tests)
    public void Dispose();      // stops every session (app exit); returns after RequestStop, disposal off-thread
}

public sealed class SharedStreamLease : IDisposable
{
    public StreamSession Session { get; }
    public void SetTargetSize(int width, int height);  // session decodes at the max over live leases
    public Task ReleaseAsync();                        // idempotent; completes when the session is disposed if this was the last lease, else at once
    public void Dispose();                             // = _ = ReleaseAsync()
}
```

- Sessions are keyed by `(key, url, useUdp)`: after an edit changes the URL, a new lease gets a new session even if an old lease still holds the old one.
- Thread-safe (lock); `onCreated` and `Start()` run outside the lock is not required but must not deadlock if `onCreated` subscribes events.

- [ ] **Step 1: Failing tests** (collection `"rtsp"`, skip like other RTSP tests):

```csharp
[SkippableFact]
public void Two_leases_share_one_session_and_both_read_frames()
{
    using var streams = Open(out var url);
    using var a = streams.Acquire(Key, url, false, 320, 180);
    using var b = streams.Acquire(Key, url, false, 640, 360);
    Assert.Same(a.Session, b.Session);
    Assert.Equal(1, streams.Count);
    long seqA = 0, seqB = 0;
    Assert.True(TestUtil.WaitFor(() => a.Session.Mailbox.TryRead(ref seqA, _ => { }), TimeSpan.FromSeconds(10)));
    Assert.True(b.Session.Mailbox.TryRead(ref seqB, _ => { }) || TestUtil.WaitFor(() => b.Session.Mailbox.TryRead(ref seqB, _ => { }), TimeSpan.FromSeconds(2)));
}

[SkippableFact]
public void Session_decodes_at_the_largest_requested_size()
{
    using var streams = Open(out var url);
    using var small = streams.Acquire(Key, url, false, 160, 90);
    using var big = streams.Acquire(Key, url, false, 640, 360);
    VideoFrame? last = null;
    long seq = 0;
    Assert.True(TestUtil.WaitFor(() => big.Session.Mailbox.TryRead(ref seq, f => last = f) && last!.Width == 640, TimeSpan.FromSeconds(10)));
}

[SkippableFact]
public async Task Last_release_stops_the_session_and_an_earlier_release_does_not()
{
    using var streams = Open(out var url);
    var a = streams.Acquire(Key, url, false, 320, 180);
    var b = streams.Acquire(Key, url, false, 320, 180);
    Assert.True(TestUtil.WaitFor(() => a.Session.State == SessionState.Playing, TimeSpan.FromSeconds(10)));
    await a.ReleaseAsync();
    Assert.Equal(SessionState.Playing, b.Session.State);
    await b.ReleaseAsync();
    Assert.Equal(SessionState.Stopped, b.Session.State);
    Assert.Equal(0, streams.Count);
}

[SkippableFact]
public void Different_url_for_the_same_key_gets_its_own_session()
{
    using var streams = Open(out var url);
    using var a = streams.Acquire(Key, url, false, 320, 180);
    using var b = streams.Acquire(Key, server.Url("av"), false, 320, 180);
    Assert.NotSame(a.Session, b.Session);
}

[SkippableFact]
public void OnCreated_runs_once_per_session()
{
    using var streams = Open(out var url);
    var created = 0;
    using var a = streams.Acquire(Key, url, false, 320, 180, _ => created++);
    using var b = streams.Acquire(Key, url, false, 320, 180, _ => created++);
    Assert.Equal(1, created);
}

[SkippableFact]
public void Detector_sees_motion_in_the_moving_test_pattern()
{
    using var streams = Open(out var url);
    using var lease = streams.Acquire(Key, url, false, 320, 180);
    var detector = new FrameDiffDetector(MotionSensitivity.High);
    var gray = GrayScaler.Create();
    long seq = 0;
    var detected = TestUtil.WaitFor(() =>
    {
        Thread.Sleep(200);
        return lease.Session.Mailbox.TryRead(ref seq, f => GrayScaler.FromBgra(f.Data, f.Width, f.Height, f.Stride, gray))
            && detector.Analyze(gray).Motion;
    }, TimeSpan.FromSeconds(10));
    Assert.True(detected);
}
```

with helpers `static readonly Guid Key = Guid.NewGuid();` and `SharedStreams Open(out string url) { Skip.If(...); FFmpegLoader.Initialize(); url = server.Url("open"); return new SharedStreams(); }` (the class takes `RtspTestServer server` via primary constructor like other RTSP tests).

(`testsrc2` has moving elements; if High does not trigger reliably within 10 s, investigate before weakening — check the fraction values in a debug print and report.)

- [ ] **Step 2: Run** — build errors. **Step 3: Implement** `SharedStreams` per the interface (entry = session + list of leases + lease sizes; target = max width/height over live leases; release removes the lease, recomputes, and if empty removes the entry, `RequestStop()` and `Task.Run(session.Dispose)` returned as the task). **Step 4: Run** Media tests (full suite once, twice at the end) — pass. **Step 5: Commit**

```bash
git add src/Centinela.Media/SharedStreams.cs tests/Centinela.Media.Tests/SharedStreamsTests.cs
git commit -m "feat(media): shared substream sessions handed out as leases"
```

---

### Task 6: Tiles use the shared substream (App)

**Files:**
- Modify: `src/Centinela.App/CameraTile.xaml.cs`, `src/Centinela.App/MainWindow.View.cs`, `src/Centinela.App/MainWindow.xaml.cs`, `src/Centinela.App/MainWindow.Errors.cs`
- Create: `src/Centinela.App/MainWindow.Streams.cs`

**Interfaces:**
- Consumes: `SharedStreams`, `SharedStreamLease` (Task 5).
- Produces: `MainWindow.AcquireSub(Camera camera, int width, int height) : SharedStreamLease?` (null when the camera has no usable Sub URL) — used by tiles and by `MotionService` (Task 7). It wires the shared session's `ErrorOccurred`/`StateChanged(Playing)` to `ErrorCenter` exactly once per session (`onCreated`), marshalled to the UI thread, using the current camera object for that Id.
- `CameraTile(Camera camera, StreamKind kind, bool manage = true, Func<Camera, SharedStreamLease?>? acquireShared = null)`: when `kind == Sub` and `acquireShared` is given, the tile uses a lease instead of creating its own session.

Behaviour to implement:
1. `MainWindow` owns `readonly SharedStreams _streams = new();`. `AcquireSub` builds the Sub URL with `StreamUrlBuilder.Build(camera, StreamKind.Sub)` (catch `InvalidOperationException` → null) and calls `_streams.Acquire(camera.Id, url, camera.UseUdp, w, h, onCreated)`.
2. In `CameraTile`, keep `_session` as the session in use (own or leased). For a leased session: subscribe to its events with stored delegates and unsubscribe them in `ShutdownAsync`; show the session's current `State` right away (it may already be `Playing`), and `UpdateAudioButton` after attach; `UpdateTargetSize` calls `lease.SetTargetSize`; `ShutdownAsync` returns `lease.ReleaseAsync()` instead of disposing the session; never call `SetAudioSink` on a leased session (Sub tiles are never audio-capable; guard anyway).
3. Error reporting: tiles on leased sessions still show errors on screen, but do **not** raise `ErrorReported`/`PlayingReached` (the window reports once per shared session from `onCreated`). Tiles with their own session (Main, fullscreen) keep reporting as today. Keep `StreamFailed` (placeholder retirement) for both.
4. `CreateTile` passes `acquireShared: c => AcquireSub(c, 0, 0)` for Sub tiles (the tile sets its real size via `SetTargetSize` on `SizeChanged`).
5. `OnClosed`: after the existing wait, `_streams.Dispose()` (it must not block the UI for long: `RequestStop` all, dispose off-thread; include those tasks in the bounded wait if simple).
6. Snapshot from a Sub tile keeps working (it uses `_session` as fallback).
7. No behaviour change for the user in this task: same views, same errors, one connection per camera substream.

Verify: build into scratch (0 warnings); Core + Media tests; smoke run with `CENTINELA_DATA_DIR` (alive 6 s, stop only your PID). If mediamtx is available, add custom cameras `rtsp://127.0.0.1:18554/open` in the smoke data folder's `cameras.json` (via the app is not possible headless — write the JSON file directly in the smoke folder before launching) and check with `netstat -ano | findstr 18554` that one Sub camera shown in the grid opens exactly one connection. Report the manual checks for the user.

Commit:

```bash
git add src/Centinela.App/CameraTile.xaml.cs src/Centinela.App/MainWindow.View.cs src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.Errors.cs src/Centinela.App/MainWindow.Streams.cs
git commit -m "feat(app): substream tiles use shared sessions"
```

---

### Task 7: Motion service, red frame and 👁 (App)

**Files:**
- Create: `src/Centinela.App/MotionService.cs`, `src/Centinela.App/MainWindow.Motion.cs`
- Modify: `src/Centinela.App/CameraTile.xaml`, `src/Centinela.App/CameraTile.xaml.cs`, `src/Centinela.App/MainWindow.xaml.cs`, `src/Centinela.App/MainWindow.View.cs`, `src/Centinela.App/MainWindow.Menu.cs` (import applies motion)

**Interfaces:**
- Consumes: `AcquireSub` (Task 6), `GrayScaler`, `FrameDiffDetector`, `IMotionDetector`, `MotionTracker`, `MotionUpdate`, `MotionEvent` (Tasks 2–3), `NotificationGate` (Task 3), `TrayController.ShowBalloon(title, text, onClick)`.
- Produces:
  - `sealed class MotionService : IDisposable` — ctor `(Func<Camera, SharedStreamLease?> acquire, Dispatcher dispatcher, Func<MotionSensitivity, IMotionDetector>? detectorFactory = null)` (default `s => new FrameDiffDetector(s)`); `void Apply(IReadOnlyList<Camera> cameras)` (UI thread: start monitors for `MotionEnabled` cameras, restart when URL/UDP/sensitivity change, update cooldown in place, stop the rest with `Flush`); `bool IsActive(Guid id)`; events raised on the UI thread: `MotionChanged(Guid id, bool active)`, `MotionStarted(Camera camera, bool alert)`, `MotionEnded(Camera camera, MotionEvent motionEvent)`, `DetectorFailed(Camera camera, string message)`; `Dispose()` flushes active events (raising `MotionEnded` synchronously on the calling UI thread) and releases leases.
  - `CameraTile.SetMotionActive(bool)`, `CameraTile.SetMotionEnabled(bool)`, `event Action<CameraTile>? MotionToggleRequested`.

Behaviour:
1. `MotionService` runs one `System.Threading.Timer` every 200 ms; a reentrancy guard skips a tick if the previous one is still running. Per monitor: if `lease.Session.State != Playing` → feed `new MotionSample(false, 0)` to the tracker (so events end) and mark "needs reset"; when it is Playing again call `detector.Reset()` first. Otherwise `Mailbox.TryRead(ref seq, f => GrayScaler.FromBgra(f.Data, f.Width, f.Height, f.Stride, gray))` (the only work inside the lock), then `detector.Analyze(gray)` and `tracker.Update(sample, DateTimeOffset.Now)` outside it. Post transitions with `dispatcher.BeginInvoke`. `MotionChanged` fires on Started (true) and Ended (false). An exception from the detector → stop that monitor, post `DetectorFailed` (message sanitized: it contains no credentials anyway; never include URLs).
2. Monitor leases use size 320×180 (`acquire` from MainWindow: `c => AcquireSub(c, 320, 180)`); a camera without a usable Sub URL is skipped.
3. `MainWindow.Motion.cs`: create the service in the constructor after `InitErrors`/`InitTray` (always, also when starting hidden); call `_motion.Apply(_cameras)` at startup and after every change to the camera list (add, edit, delete, duplicate, import, options). Handlers:
   - `MotionChanged` → `SetMotionActive` on every tile of that camera (`TilesOf(id)`); new tiles get `SetMotionActive(_motion.IsActive(id))` and `SetMotionEnabled(camera.MotionEnabled)` in `CreateTile` / `ShowFullscreen`.
   - `MotionStarted(camera, alert)` → if `alert`: `NotificationGate.Decide(NoticeKind.Motion, camera, _settings, WindowVisible, DateTime.Now)`; `Show` → `_tray.ShowBalloon($"Detección de movimiento: «{camera.Name}»", DateTime.Now.ToString("HH:mm:ss"), ShowFromTray)`; `PlaySound` → `SystemSounds.Asterisk.Play()`. `WindowVisible` = `!IsHiddenInTray && IsVisible && WindowState != WindowState.Minimized`.
   - `MotionEnded` → handled in Task 8 (log); for now nothing.
   - `DetectorFailed` → `_errorLog.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Movimiento", $"{camera.Name}: la detección de movimiento se ha desactivado", message))` and set nothing else (the monitor is already stopped until the next `Apply` with changed options).
   - `MotionToggleRequested(tile)` → find the current camera by Id, flip `MotionEnabled`, `SaveCameras()`, `_motion.Apply(_cameras)`, update `SetMotionEnabled` on its tiles.
   - `OnClosed`: `_motion.Dispose()` before `_streams.Dispose()`.
4. `CameraTile`:
   - `MotionButton` "👁" in `Actions` (manage tiles only), tooltip «Detección de movimiento: activada» / «desactivada», click → `MotionToggleRequested`.
   - Checkable context-menu item «Detección de movimiento» (manage tiles only), checked = enabled.
   - `MotionOffIndicator` next to `NameLabel`: a small 👁 with a red diagonal line over it (Grid with TextBlock + Line), visible when detection is disabled.
   - Red frame: `SetMotionActive(true)` → `Frame.BorderBrush = #E53935`, `BorderThickness = 3`; false → normal (`#2A2A2A`, 1). The drag-over highlight (DodgerBlue) must restore to the motion state, not always to normal — use one `UpdateBorder()` that combines drop-hover and motion.
   - Fullscreen tile: red frame yes; 👁 button and menu item no.

Verify: build (0 warnings), Core + Media tests, smoke (`CENTINELA_DATA_DIR`, alive 6 s, and `--tray`), manual checks listed in the report. Commit:

```bash
git add src/Centinela.App/MotionService.cs src/Centinela.App/MainWindow.Motion.cs src/Centinela.App/CameraTile.xaml src/Centinela.App/CameraTile.xaml.cs src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.View.cs src/Centinela.App/MainWindow.Menu.cs
git commit -m "feat(app): always-on motion detection with red frame, eye toggle and tray notice"
```

---

### Task 8: Motion log window (App)

**Files:**
- Create: `src/Centinela.App/MotionLogWindow.xaml`, `src/Centinela.App/MotionLogWindow.xaml.cs`
- Modify: `src/Centinela.App/MainWindow.xaml` (status bar button), `src/Centinela.App/MainWindow.Motion.cs`, `src/Centinela.App/MainWindow.xaml.cs` (dispose order)

**Interfaces:**
- Consumes: `MotionLog`, `MotionLogEntry` (Task 4); `MotionService.MotionEnded` (Task 7).

Behaviour:
1. At startup: `MotionLog.PurgeOlderThan(AppPaths.LogsDirectory, DateTime.Now)` (guarded like the error log purge) and `_motionLog = new MotionLog(AppPaths.LogsDirectory)`.
2. `MotionEnded(camera, e)` → `_motionLog.Add(new MotionLogEntry(e.Start.LocalDateTime, camera.Name, e.End - e.Start, e.Peak))`, increment an unread counter, update the button text «Movimiento» / «Movimiento (n)».
3. Status bar: a `MotionLogButton` «Movimiento» next to «Registro» (tooltip «Registro de movimiento»); click opens (or activates) `MotionLogWindow` and resets the counter.
4. `MotionLogWindow` mirrors `ErrorLogWindow`: newest first, follows new entries while open (`EntryAdded` marshalled with `Dispatcher.BeginInvoke`, unsubscribed on close), DataGrid columns «Hora» (`Start`, `dd/MM HH:mm:ss`), «Cámara», «Duración» (same format as the log line), «Cambio máx.» (`Peak` as `0.0 %`), buttons «Copiar» (selected or all, `MotionLog.FormatLine`), «Abrir carpeta», «Vaciar».
5. `OnClosed`: `_motion.Dispose()` (flushes → `MotionEnded` → logged) happens **before** `_motionLog.Dispose()`.

Verify: build, tests, smoke. Commit:

```bash
git add src/Centinela.App/MotionLogWindow.xaml src/Centinela.App/MotionLogWindow.xaml.cs src/Centinela.App/MainWindow.xaml src/Centinela.App/MainWindow.Motion.cs src/Centinela.App/MainWindow.xaml.cs
git commit -m "feat(app): motion log with its own window and unread counter"
```

---

### Task 9: Notification options window and gated connection notices (App)

**Files:**
- Create: `src/Centinela.App/NotificationOptionsWindow.xaml`, `src/Centinela.App/NotificationOptionsWindow.xaml.cs`
- Modify: `src/Centinela.App/ErrorCenter.cs`, `src/Centinela.App/MainWindow.Errors.cs`, `src/Centinela.App/MainWindow.xaml` (menu item), `src/Centinela.App/MainWindow.Menu.cs` or `MainWindow.Motion.cs` (apply options)

**Interfaces:**
- Consumes: `NotificationGate`, `NoticeKind`, `NoticeDecision`, `AppSettings` sound/quiet fields, `Camera` motion/alert fields, `Camera.AllowedCooldowns`, `AppSettings.TryParseTime`.
- Produces: `ErrorCenter(ErrorLog log, Func<NoticeKind, Camera, NoticeDecision> decide)`; `event Action<NoticeKind>? SoundRequested`.

Behaviour:
1. `ErrorCenter.Report`: logging and unread count unchanged; when the throttling policy says notify, call `decide(NoticeKind.ConnectionLost, camera)`; `Show` → `ToastRequested`; `PlaySound` → `SoundRequested(ConnectionLost)`. `Playing`: when the policy reports a recovery, the log line is written as today; the toast only if `decide(NoticeKind.ConnectionRecovered, camera).Show`.
2. `MainWindow.InitErrors`: `decide = (kind, camera) => NotificationGate.Decide(kind, CurrentCamera(camera), _settings, WindowVisible, DateTime.Now)` where `CurrentCamera` looks the camera up by Id in `_cameras` (the tile may hold an older copy; fall back to the given one). `SoundRequested(ConnectionLost)` → `SystemSounds.Exclamation.Play()`.
3. Menu **Ver › Opciones de avisos…** opens `NotificationOptionsWindow` (modal, owner = window) built from copies of the cameras and settings:
   - DataGrid, one row per camera (by `Order`): «Cámara» (read-only), «Detección» (checkbox), «Sensibilidad» (Baja/Media/Alta), «Espera» (30 s / 1 min / 5 min / 15 min from `Camera.AllowedCooldowns`), «Avisos de conexión» (checkbox), «Avisos de movimiento» (checkbox).
   - Checkboxes «Sonar al perder una conexión», «Sonar al detectar movimiento».
   - «Horas de silencio»: checkbox + two text boxes `HH:mm`; OK is refused (red border + message) while either is invalid (`AppSettings.TryParseTime`).
   - The note text from the spec: «La detección mantiene abierta la conexión de baja calidad de cada cámara vigilada, también con la app en la bandeja.»
   - «Aceptar» / «Cancelar». Cancel changes nothing.
4. On Aceptar: copy the row values into the matching `_cameras` entries (by Id), copy the settings values, `SaveCameras()` (includes the automatic copy), `SaveSettingsQuietly()`, `_motion.Apply(_cameras)`, refresh `SetMotionEnabled` on visible tiles. Cameras deleted while the window was open are ignored.

Verify: build, tests, smoke. Commit:

```bash
git add src/Centinela.App/NotificationOptionsWindow.xaml src/Centinela.App/NotificationOptionsWindow.xaml.cs src/Centinela.App/ErrorCenter.cs src/Centinela.App/MainWindow.Errors.cs src/Centinela.App/MainWindow.xaml src/Centinela.App/MainWindow.Menu.cs src/Centinela.App/MainWindow.Motion.cs
git commit -m "feat(app): notification options (per camera, sounds, quiet hours) and gated notices"
```

---

### Task 10: Documentation

**Files:** Modify `README.md`, `src/Centinela.App/InfoDocuments.cs`.

- README features: motion detection (always on for cameras with 👁 on, also in the tray; red frame; tray notice only when the window is not visible; cooldown; sensitivity; «Movimiento» log with daily files `logs\movimiento-*.log`, 14 days), notification options window (per camera, sounds, quiet hours crossing midnight), consumption note (one low-quality connection per watched camera), new data locations. Update the data section and test counts.
- Manual (`InfoDocuments.Manual()`): short paragraphs in the same voice for «Detección de movimiento», «Registro de movimiento», «Opciones de avisos».
- Build (0 warnings), Core + Media tests. Commit:

```bash
git add README.md src/Centinela.App/InfoDocuments.cs
git commit -m "docs: v1.3 motion detection and notification options"
```
