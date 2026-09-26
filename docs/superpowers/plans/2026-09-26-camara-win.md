# CamaraWin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows WPF app that shows Tapo/Imou/generic RTSP cameras in a grid with minimum latency, with ONVIF discovery, fullscreen, drag-to-reorder, snapshots and manual MKV recording.

**Architecture:** Three projects. `CamaraWin.Core` (model, URL rules, persistence, grid math, ONVIF) has no UI and no FFmpeg. `CamaraWin.Media` wraps FFmpeg through FFmpeg.AutoGen: one thread per RTSP session, GPU decode, BGRA scaling into a single-slot `FrameMailbox`, a packet-remux `Recorder` and a PNG `SnapshotWriter`. `CamaraWin.App` (WPF) pulls the latest frame on every `CompositionTarget.Rendering` tick into a `WriteableBitmap`, with no clock and no queue.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, FFmpeg.AutoGen 9.0.1.1 + FFmpeg 9.0 LGPL shared DLLs (BtbN), xUnit 2 + Xunit.SkippableFact, mediamtx (test RTSP server), System.Security.Cryptography.ProtectedData.

**Spec:** `docs/superpowers/specs/2026-09-26-camara-win-design.md`

## Global Constraints

- Target framework for every project: `net10.0-windows` (set once in `Directory.Build.props`; remove `<TargetFramework>` lines from generated csproj files).
- Solution file: `CamaraWin.slnx` (the .NET 10 SDK default).
- FFmpeg.AutoGen **9.0.1.1**, API in namespace `FFmpeg.AutoGen` (`ffmpeg` static class). Loading: `ffmpeg.RootPath = dir; DynamicallyLoadedBindings.Initialize();`. Expected DLLs: `avcodec-63.dll`, `avformat-63.dll`, `avutil-61.dll`, `swscale-10.dll`, `swresample-7.dll` in `<output>\ffmpeg\`.
- FFmpeg download URL: `https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip`. Only LGPL builds may be shipped.
- `ffmpeg/` and `tools/bin/` are git-ignored. **Ask the user before running any download script.**
- Passwords are never written in plaintext and never logged. DPAPI `CurrentUser` scope.
- Credentials inside RTSP URLs are escaped with `Uri.EscapeDataString`.
- Data file: `%AppData%\CamaraWin\cameras.json`; settings: `%AppData%\CamaraWin\settings.json`.
- Recordings: `%USERPROFILE%\Videos\CamaraWin\<camera>\yyyy-MM-dd_HH-mm-ss.mkv`. Snapshots: `%USERPROFILE%\Pictures\CamaraWin\<camera>_yyyy-MM-dd_HH-mm-ss.png`.
- UI text is Spanish. Code identifiers and comments are English.
- Live-view options: `rtsp_transport=tcp|udp`, `fflags=nobuffer`, `probesize=32768`, `max_delay=0`, `reorder_queue_size=0`, `timeout=5000000`; **no** `avformat_find_stream_info` for decoding sessions. Decoder: `AV_CODEC_FLAG_LOW_DELAY`, slice threads, 2 threads, D3D11VA with silent software fallback.
- Reconnect backoff: 1, 2, 4, 8, 10, 10… seconds. 5 s without packets = stall. Auth errors do not retry.
- Every commit message ends with a blank line followed by `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Map

```
CamaraWin.slnx
Directory.Build.props             shared TFM / nullable / usings
Directory.Build.targets           copies ffmpeg\bin\*.dll into output when CopyFFmpeg=true
tools/get-ffmpeg.ps1              downloads FFmpeg LGPL shared → ffmpeg/bin
tools/get-mediamtx.ps1            downloads mediamtx → tools/bin (tests only)
src/CamaraWin.Core/
  Camera.cs                       Brand, StreamKind, Camera
  StreamUrlBuilder.cs             brand → RTSP URL
  AppPaths.cs                     data/recordings/snapshot paths, filename sanitizing
  CameraStore.cs                  cameras.json + DPAPI
  Settings.cs                     GridMode, AppSettings, SettingsStore
  GridLayout.cs                   GridSize, GridLayout
  Onvif/WsDiscovery.cs            DiscoveredDevice, probe build/parse/send
  Onvif/OnvifClient.cs            SOAP client, parsers, exceptions
  Onvif/BrandInference.cs         manufacturer → Brand
src/CamaraWin.Media/
  FFmpegLoader.cs                 DLL loading + version
  FFmpegException.cs              error text, auth detection
  FrameMailbox.cs                 VideoFrame, FrameMailbox
  FrameGeometry.cs                FitSize
  StreamSession.cs                RTSP session thread (decode + record + snapshot)
  Recorder.cs                     packet remux → MKV
  SnapshotWriter.cs               AVFrame → PNG
src/CamaraWin.App/
  App.xaml(.cs)                   startup, FFmpeg init, styles
  MainWindow.xaml(.cs)            toolbar, grid, status bar, persistence
  CameraTile.xaml(.cs)            video + overlay + actions + drag/drop
  AddCameraDialog.xaml(.cs)       add/edit + live test
  FullscreenWindow.xaml(.cs)      mainstream fullscreen
  DiscoveryDialog.xaml(.cs)       ONVIF scan
tests/CamaraWin.Core.Tests/       unit tests
tests/CamaraWin.Media.Tests/      FFmpeg unit + RTSP integration tests
  Rtsp/RtspTestServer.cs          mediamtx + ffmpeg publisher fixture
README.md, LICENSE, THIRD-PARTY-NOTICES.md
```

---

### Task 1: Solution scaffold, camera model and RTSP URL rules

**Files:**
- Create: `CamaraWin.slnx`, `Directory.Build.props`, `src/CamaraWin.Core/CamaraWin.Core.csproj`, `src/CamaraWin.Core/Camera.cs`, `src/CamaraWin.Core/StreamUrlBuilder.cs`
- Create: `tests/CamaraWin.Core.Tests/CamaraWin.Core.Tests.csproj`, `tests/CamaraWin.Core.Tests/StreamUrlBuilderTests.cs`

**Interfaces:**
- Produces: `enum Brand { Tapo, Imou, Custom }`, `enum StreamKind { Main, Sub }`, `class Camera` (properties `Id, Name, Brand, Host, Port, User, Password, MainUrlOverride, SubUrlOverride, UseUdp, Order`, method `Camera Clone()`), `static string StreamUrlBuilder.Build(Camera, StreamKind)`, `static string StreamUrlBuilder.InjectCredentials(string url, string user, string password)`.

- [ ] **Step 1: Scaffold**

Run from the repo root (`X:\project1\camara_win`):

```bash
dotnet new sln -n CamaraWin
dotnet new classlib -n CamaraWin.Core -o src/CamaraWin.Core
dotnet new xunit -n CamaraWin.Core.Tests -o tests/CamaraWin.Core.Tests
dotnet sln CamaraWin.slnx add src/CamaraWin.Core tests/CamaraWin.Core.Tests
dotnet add tests/CamaraWin.Core.Tests reference src/CamaraWin.Core
rm src/CamaraWin.Core/Class1.cs tests/CamaraWin.Core.Tests/UnitTest1.cs
sed -i '/<TargetFramework>/d' src/CamaraWin.Core/CamaraWin.Core.csproj tests/CamaraWin.Core.Tests/CamaraWin.Core.Tests.csproj
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests** — `tests/CamaraWin.Core.Tests/StreamUrlBuilderTests.cs`

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class StreamUrlBuilderTests
{
    static Camera Cam(Brand brand) => new()
    {
        Name = "cam", Brand = brand, Host = "192.168.1.20", User = "user", Password = "pass",
    };

    [Theory]
    [InlineData(StreamKind.Main, "rtsp://user:pass@192.168.1.20:554/stream1")]
    [InlineData(StreamKind.Sub, "rtsp://user:pass@192.168.1.20:554/stream2")]
    public void Tapo_urls(StreamKind kind, string expected) =>
        Assert.Equal(expected, StreamUrlBuilder.Build(Cam(Brand.Tapo), kind));

    [Theory]
    [InlineData(StreamKind.Main, "rtsp://user:pass@192.168.1.20:554/cam/realmonitor?channel=1&subtype=0")]
    [InlineData(StreamKind.Sub, "rtsp://user:pass@192.168.1.20:554/cam/realmonitor?channel=1&subtype=1")]
    public void Imou_urls(StreamKind kind, string expected) =>
        Assert.Equal(expected, StreamUrlBuilder.Build(Cam(Brand.Imou), kind));

    [Fact]
    public void Custom_port_is_used()
    {
        var cam = Cam(Brand.Tapo);
        cam.Port = 8554;
        Assert.Equal("rtsp://user:pass@192.168.1.20:8554/stream1", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Special_characters_in_credentials_are_escaped()
    {
        var cam = Cam(Brand.Tapo);
        cam.User = "ad min";
        cam.Password = "p@ss:w/rd";
        Assert.Equal("rtsp://ad%20min:p%40ss%3Aw%2Frd@192.168.1.20:554/stream1",
            StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Empty_user_omits_credentials()
    {
        var cam = Cam(Brand.Tapo);
        cam.User = "";
        Assert.Equal("rtsp://192.168.1.20:554/stream1", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Override_wins_and_gets_credentials()
    {
        var cam = Cam(Brand.Tapo);
        cam.MainUrlOverride = "rtsp://192.168.1.20:554/onvif/profile1";
        Assert.Equal("rtsp://user:pass@192.168.1.20:554/onvif/profile1", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Sub_falls_back_to_main_override()
    {
        var cam = Cam(Brand.Custom);
        cam.MainUrlOverride = "rtsp://host/x";
        Assert.Equal("rtsp://user:pass@host/x", StreamUrlBuilder.Build(cam, StreamKind.Sub));
    }

    [Fact]
    public void Override_with_existing_credentials_is_untouched()
    {
        var cam = Cam(Brand.Custom);
        cam.MainUrlOverride = "rtsp://a:b@host/x";
        Assert.Equal("rtsp://a:b@host/x", StreamUrlBuilder.Build(cam, StreamKind.Main));
    }

    [Fact]
    public void Custom_without_override_throws()
    {
        Assert.Throws<InvalidOperationException>(() => StreamUrlBuilder.Build(Cam(Brand.Custom), StreamKind.Main));
    }

    [Fact]
    public void Clone_copies_values_and_keeps_id()
    {
        var cam = Cam(Brand.Imou);
        var copy = cam.Clone();
        Assert.NotSame(cam, copy);
        Assert.Equal(cam.Id, copy.Id);
        Assert.Equal(cam.Password, copy.Password);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/CamaraWin.Core.Tests`
Expected: build FAILS with `The type or namespace name 'Camera' could not be found`.

- [ ] **Step 4: Implement** — `src/CamaraWin.Core/Camera.cs`

```csharp
namespace CamaraWin.Core;

public enum Brand { Tapo, Imou, Custom }

public enum StreamKind { Main, Sub }

public sealed class Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Brand Brand { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 554;
    public string User { get; set; } = "";
    /// <summary>Plaintext, in memory only. Encrypted by CameraStore on disk.</summary>
    public string Password { get; set; } = "";
    public string? MainUrlOverride { get; set; }
    public string? SubUrlOverride { get; set; }
    public bool UseUdp { get; set; }
    public int Order { get; set; }

    public Camera Clone() => (Camera)MemberwiseClone();
}
```

`src/CamaraWin.Core/StreamUrlBuilder.cs`:

```csharp
namespace CamaraWin.Core;

public static class StreamUrlBuilder
{
    public static string Build(Camera camera, StreamKind kind)
    {
        var overrideUrl = kind == StreamKind.Main
            ? camera.MainUrlOverride
            : camera.SubUrlOverride ?? camera.MainUrlOverride;
        if (!string.IsNullOrWhiteSpace(overrideUrl))
            return InjectCredentials(overrideUrl.Trim(), camera.User, camera.Password);

        var path = (camera.Brand, kind) switch
        {
            (Brand.Tapo, StreamKind.Main) => "/stream1",
            (Brand.Tapo, StreamKind.Sub) => "/stream2",
            (Brand.Imou, StreamKind.Main) => "/cam/realmonitor?channel=1&subtype=0",
            (Brand.Imou, StreamKind.Sub) => "/cam/realmonitor?channel=1&subtype=1",
            _ => throw new InvalidOperationException("A custom camera needs a main RTSP URL."),
        };
        return $"rtsp://{Credentials(camera.User, camera.Password)}{camera.Host}:{camera.Port}{path}";
    }

    /// <summary>Adds user:password@ to a URL that has no credentials yet.</summary>
    public static string InjectCredentials(string url, string user, string password)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0 || user.Length == 0) return url;
        var rest = url[(schemeEnd + 3)..];
        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        if (authority.Contains('@')) return url;
        return url[..(schemeEnd + 3)] + Credentials(user, password) + rest;
    }

    static string Credentials(string user, string password) =>
        user.Length == 0 ? "" : $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@";
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CamaraWin.Core.Tests`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add CamaraWin.slnx Directory.Build.props src tests
git commit -m "feat(core): camera model and RTSP URL rules for Tapo/Imou/custom

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Paths, camera store (DPAPI) and settings store

**Files:**
- Create: `src/CamaraWin.Core/AppPaths.cs`, `src/CamaraWin.Core/CameraStore.cs`, `src/CamaraWin.Core/Settings.cs`
- Test: `tests/CamaraWin.Core.Tests/AppPathsTests.cs`, `tests/CamaraWin.Core.Tests/CameraStoreTests.cs`, `tests/CamaraWin.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `Camera`, `Brand` (Task 1).
- Produces: `static class AppPaths { string DataDirectory; string RecordingsDirectory; string SnapshotsDirectory; string SanitizeFileName(string); string RecordingFile(string cameraName, DateTime time); string SnapshotFile(string cameraName, DateTime time); }`; `sealed class CameraStore(string filePath) { static string DefaultPath; IReadOnlyList<Camera> Load(); void Save(IEnumerable<Camera>); }`; `enum GridMode { Auto = 0, One = 1, Four = 4, Nine = 9, Sixteen = 16 }`; `sealed class AppSettings { double? Left; double? Top; double Width = 1280; double Height = 800; bool Maximized; GridMode GridMode; }`; `sealed class SettingsStore(string filePath) { static string DefaultPath; AppSettings Load(); void Save(AppSettings); }`.

- [ ] **Step 1: Add DPAPI package**

```bash
dotnet add src/CamaraWin.Core package System.Security.Cryptography.ProtectedData
```

- [ ] **Step 2: Write the failing tests**

`tests/CamaraWin.Core.Tests/AppPathsTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class AppPathsTests
{
    [Theory]
    [InlineData("Salón", "Salón")]
    [InlineData("Garaje: puerta/1", "Garaje_ puerta_1")]
    [InlineData("  ", "camara")]
    [InlineData("a*b?c", "a_b_c")]
    public void SanitizeFileName(string input, string expected) =>
        Assert.Equal(expected, AppPaths.SanitizeFileName(input));

    [Fact]
    public void RecordingFile_uses_camera_folder_and_timestamp()
    {
        var path = AppPaths.RecordingFile("Jardín", new DateTime(2026, 9, 26, 20, 15, 3));
        Assert.Equal(Path.Combine(AppPaths.RecordingsDirectory, "Jardín", "2026-09-26_20-15-03.mkv"), path);
    }

    [Fact]
    public void SnapshotFile_uses_camera_prefix_and_timestamp()
    {
        var path = AppPaths.SnapshotFile("Jardín", new DateTime(2026, 9, 26, 20, 15, 3));
        Assert.Equal(Path.Combine(AppPaths.SnapshotsDirectory, "Jardín_2026-09-26_20-15-03.png"), path);
    }
}
```

`tests/CamaraWin.Core.Tests/CameraStoreTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public sealed class CameraStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-tests-" + Guid.NewGuid());
    string FilePath => Path.Combine(_dir, "cameras.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static Camera Sample(int order) => new()
    {
        Name = $"Cam {order}", Brand = Brand.Imou, Host = "192.168.1.30", Port = 8554,
        User = "admin", Password = "SuperSecreta123", MainUrlOverride = "rtsp://h/main",
        SubUrlOverride = "rtsp://h/sub", UseUdp = true, Order = order,
    };

    [Fact]
    public void Load_returns_empty_when_file_missing() =>
        Assert.Empty(new CameraStore(FilePath).Load());

    [Fact]
    public void Round_trip_preserves_all_fields()
    {
        var original = Sample(3);
        new CameraStore(FilePath).Save([original]);

        var loaded = Assert.Single(new CameraStore(FilePath).Load());
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Brand, loaded.Brand);
        Assert.Equal(original.Host, loaded.Host);
        Assert.Equal(original.Port, loaded.Port);
        Assert.Equal(original.User, loaded.User);
        Assert.Equal(original.Password, loaded.Password);
        Assert.Equal(original.MainUrlOverride, loaded.MainUrlOverride);
        Assert.Equal(original.SubUrlOverride, loaded.SubUrlOverride);
        Assert.Equal(original.UseUdp, loaded.UseUdp);
        Assert.Equal(original.Order, loaded.Order);
    }

    [Fact]
    public void Password_is_not_stored_in_plaintext()
    {
        new CameraStore(FilePath).Save([Sample(0)]);
        var json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("SuperSecreta123", json);
        Assert.Contains("passwordProtected", json);
    }

    [Fact]
    public void Load_returns_cameras_sorted_by_order()
    {
        new CameraStore(FilePath).Save([Sample(2), Sample(0), Sample(1)]);
        Assert.Equal(new[] { 0, 1, 2 }, new CameraStore(FilePath).Load().Select(c => c.Order));
    }
}
```

`tests/CamaraWin.Core.Tests/SettingsStoreTests.cs`:

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-tests-" + Guid.NewGuid());
    string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(GridMode.Auto, s.GridMode);
        Assert.Equal(1280, s.Width);
        Assert.Null(s.Left);
    }

    [Fact]
    public void Round_trip()
    {
        new SettingsStore(FilePath).Save(new AppSettings
        {
            Left = 10, Top = 20, Width = 900, Height = 600, Maximized = true, GridMode = GridMode.Nine,
        });
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal((10d, 20d, 900d, 600d, true, GridMode.Nine),
            (s.Left!.Value, s.Top!.Value, s.Width, s.Height, s.Maximized, s.GridMode));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Equal(GridMode.Auto, new SettingsStore(FilePath).Load().GridMode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/CamaraWin.Core.Tests`
Expected: build FAILS (`AppPaths`, `CameraStore`, `SettingsStore` not found).

- [ ] **Step 4: Implement**

`src/CamaraWin.Core/AppPaths.cs`:

```csharp
namespace CamaraWin.Core;

public static class AppPaths
{
    const string AppFolder = "CamaraWin";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

    public static string RecordingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), AppFolder);

    public static string SnapshotsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppFolder);

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "camara" : cleaned;
    }

    public static string RecordingFile(string cameraName, DateTime time) =>
        Path.Combine(RecordingsDirectory, SanitizeFileName(cameraName), $"{time:yyyy-MM-dd_HH-mm-ss}.mkv");

    public static string SnapshotFile(string cameraName, DateTime time) =>
        Path.Combine(SnapshotsDirectory, $"{SanitizeFileName(cameraName)}_{time:yyyy-MM-dd_HH-mm-ss}.png");
}
```

`src/CamaraWin.Core/CameraStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamaraWin.Core;

public sealed class CameraStore(string filePath)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "cameras.json");

    public IReadOnlyList<Camera> Load()
    {
        if (!File.Exists(filePath)) return [];
        var dtos = JsonSerializer.Deserialize<List<CameraDto>>(File.ReadAllText(filePath), Json) ?? [];
        return dtos.Select(FromDto).OrderBy(c => c.Order).ToList();
    }

    public void Save(IEnumerable<Camera> cameras)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cameras.Select(ToDto).ToList(), Json));
        File.Move(tmp, filePath, overwrite: true);
    }

    static CameraDto ToDto(Camera c) => new()
    {
        Id = c.Id, Name = c.Name, Brand = c.Brand, Host = c.Host, Port = c.Port, User = c.User,
        PasswordProtected = Protect(c.Password), MainUrlOverride = c.MainUrlOverride,
        SubUrlOverride = c.SubUrlOverride, UseUdp = c.UseUdp, Order = c.Order,
    };

    static Camera FromDto(CameraDto d) => new()
    {
        Id = d.Id, Name = d.Name, Brand = d.Brand, Host = d.Host, Port = d.Port, User = d.User,
        Password = Unprotect(d.PasswordProtected), MainUrlOverride = d.MainUrlOverride,
        SubUrlOverride = d.SubUrlOverride, UseUdp = d.UseUdp, Order = d.Order,
    };

    static string Protect(string plain) => plain.Length == 0
        ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    static string Unprotect(string? protectedValue) => string.IsNullOrEmpty(protectedValue)
        ? ""
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));

    sealed class CameraDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Brand Brand { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 554;
        public string User { get; set; } = "";
        public string? PasswordProtected { get; set; }
        public string? MainUrlOverride { get; set; }
        public string? SubUrlOverride { get; set; }
        public bool UseUdp { get; set; }
        public int Order { get; set; }
    }
}
```

`src/CamaraWin.Core/Settings.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamaraWin.Core;

public enum GridMode { Auto = 0, One = 1, Four = 4, Nine = 9, Sixteen = 16 }

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public bool Maximized { get; set; }
    public GridMode GridMode { get; set; } = GridMode.Auto;
}

public sealed class SettingsStore(string filePath)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, Json));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CamaraWin.Core.Tests`
Expected: PASS (all tests, including Task 1's).

- [ ] **Step 6: Commit**

```bash
git add src/CamaraWin.Core tests/CamaraWin.Core.Tests
git commit -m "feat(core): camera store with DPAPI passwords, settings and app paths

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Grid layout math

**Files:**
- Create: `src/CamaraWin.Core/GridLayout.cs`
- Test: `tests/CamaraWin.Core.Tests/GridLayoutTests.cs`

**Interfaces:**
- Consumes: `GridMode` (Task 2).
- Produces: `readonly record struct GridSize(int Rows, int Columns)`; `static GridSize GridLayout.Compute(int cameraCount, GridMode mode)`; `static int GridLayout.VisibleCount(int cameraCount, GridMode mode)`.

- [ ] **Step 1: Write the failing test** — `tests/CamaraWin.Core.Tests/GridLayoutTests.cs`

```csharp
using CamaraWin.Core;

namespace CamaraWin.Core.Tests;

public class GridLayoutTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(7, 3, 3)]
    [InlineData(10, 3, 4)]
    public void Auto_layout(int count, int rows, int cols) =>
        Assert.Equal(new GridSize(rows, cols), GridLayout.Compute(count, GridMode.Auto));

    [Theory]
    [InlineData(GridMode.One, 1)]
    [InlineData(GridMode.Four, 2)]
    [InlineData(GridMode.Nine, 3)]
    [InlineData(GridMode.Sixteen, 4)]
    public void Fixed_layout_is_square_regardless_of_count(GridMode mode, int side) =>
        Assert.Equal(new GridSize(side, side), GridLayout.Compute(7, mode));

    [Theory]
    [InlineData(7, GridMode.Auto, 7)]
    [InlineData(7, GridMode.Four, 4)]
    [InlineData(3, GridMode.Nine, 3)]
    [InlineData(7, GridMode.One, 1)]
    public void Visible_count(int count, GridMode mode, int expected) =>
        Assert.Equal(expected, GridLayout.VisibleCount(count, mode));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Core.Tests --filter FullyQualifiedName~GridLayoutTests`
Expected: build FAILS (`GridLayout` not found).

- [ ] **Step 3: Implement** — `src/CamaraWin.Core/GridLayout.cs`

```csharp
namespace CamaraWin.Core;

public readonly record struct GridSize(int Rows, int Columns);

public static class GridLayout
{
    public static int VisibleCount(int cameraCount, GridMode mode) =>
        mode == GridMode.Auto ? cameraCount : Math.Min(cameraCount, (int)mode);

    public static GridSize Compute(int cameraCount, GridMode mode)
    {
        if (mode != GridMode.Auto)
        {
            var side = (int)Math.Sqrt((int)mode);
            return new GridSize(side, side);
        }
        if (cameraCount <= 1) return new GridSize(1, 1);
        var columns = (int)Math.Ceiling(Math.Sqrt(cameraCount));
        var rows = (int)Math.Ceiling(cameraCount / (double)columns);
        return new GridSize(rows, columns);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Core.Tests --filter FullyQualifiedName~GridLayoutTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CamaraWin.Core/GridLayout.cs tests/CamaraWin.Core.Tests/GridLayoutTests.cs
git commit -m "feat(core): grid layout computation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: ONVIF WS-Discovery

**Files:**
- Create: `src/CamaraWin.Core/Onvif/WsDiscovery.cs`
- Test: `tests/CamaraWin.Core.Tests/WsDiscoveryTests.cs`

**Interfaces:**
- Produces: `sealed record DiscoveredDevice(string Host, string DeviceServiceUrl, string? Name, string? Hardware)`; `static string WsDiscovery.BuildProbe(Guid messageId)`; `static IReadOnlyList<DiscoveredDevice> WsDiscovery.ParseProbeMatches(string xml)`; `static Task<IReadOnlyList<DiscoveredDevice>> WsDiscovery.ProbeAsync(TimeSpan timeout, CancellationToken ct = default)`. Namespace `CamaraWin.Core.Onvif`.

- [ ] **Step 1: Write the failing tests** — `tests/CamaraWin.Core.Tests/WsDiscoveryTests.cs`

```csharp
using System.Xml.Linq;
using CamaraWin.Core.Onvif;

namespace CamaraWin.Core.Tests;

public class WsDiscoveryTests
{
    const string TapoMatch = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://www.w3.org/2003/05/soap-envelope"
            xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
            xmlns:wsdd="http://schemas.xmlsoap.org/ws/2005/04/discovery"
            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <SOAP-ENV:Body>
            <wsdd:ProbeMatches>
              <wsdd:ProbeMatch>
                <wsa:EndpointReference><wsa:Address>uuid:3fa1fe68-b915-4053-a3e1-c006c3afec0e</wsa:Address></wsa:EndpointReference>
                <wsdd:Types>dn:NetworkVideoTransmitter</wsdd:Types>
                <wsdd:Scopes>onvif://www.onvif.org/name/TP-IPC onvif://www.onvif.org/hardware/C200 onvif://www.onvif.org/Profile/Streaming</wsdd:Scopes>
                <wsdd:XAddrs>http://192.168.1.20:2020/onvif/device_service</wsdd:XAddrs>
                <wsdd:MetadataVersion>1</wsdd:MetadataVersion>
              </wsdd:ProbeMatch>
            </wsdd:ProbeMatches>
          </SOAP-ENV:Body>
        </SOAP-ENV:Envelope>
        """;

    [Fact]
    public void Parses_host_url_name_and_hardware()
    {
        var device = Assert.Single(WsDiscovery.ParseProbeMatches(TapoMatch));
        Assert.Equal("192.168.1.20", device.Host);
        Assert.Equal("http://192.168.1.20:2020/onvif/device_service", device.DeviceServiceUrl);
        Assert.Equal("TP-IPC", device.Name);
        Assert.Equal("C200", device.Hardware);
    }

    [Fact]
    public void Prefers_ipv4_address_and_unescapes_scopes()
    {
        var xml = TapoMatch
            .Replace("http://192.168.1.20:2020/onvif/device_service",
                     "http://[fe80::1]/onvif/device_service http://192.168.1.30/onvif/device_service")
            .Replace("name/TP-IPC", "name/IPC%20Imou");
        var device = Assert.Single(WsDiscovery.ParseProbeMatches(xml));
        Assert.Equal("192.168.1.30", device.Host);
        Assert.Equal("IPC Imou", device.Name);
    }

    [Fact]
    public void Garbage_returns_empty() =>
        Assert.Empty(WsDiscovery.ParseProbeMatches("not xml at all"));

    [Fact]
    public void Probe_is_valid_xml_with_message_id_and_type()
    {
        var id = Guid.NewGuid();
        var probe = WsDiscovery.BuildProbe(id);
        XDocument.Parse(probe);
        Assert.Contains($"uuid:{id}", probe);
        Assert.Contains("dn:NetworkVideoTransmitter", probe);
    }

    [Fact]
    public async Task ProbeAsync_completes_within_timeout()
    {
        var started = DateTime.UtcNow;
        await WsDiscovery.ProbeAsync(TimeSpan.FromMilliseconds(300));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Core.Tests --filter FullyQualifiedName~WsDiscoveryTests`
Expected: build FAILS (`WsDiscovery` not found).

- [ ] **Step 3: Implement** — `src/CamaraWin.Core/Onvif/WsDiscovery.cs`

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CamaraWin.Core.Onvif;

public sealed record DiscoveredDevice(string Host, string DeviceServiceUrl, string? Name, string? Hardware);

public static class WsDiscovery
{
    static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);
    const string ScopePrefix = "onvif://www.onvif.org/";

    public static string BuildProbe(Guid messageId) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
            xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
            xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <e:Header>
            <w:MessageID>uuid:{messageId}</w:MessageID>
            <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
            <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
          </e:Header>
          <e:Body>
            <d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe>
          </e:Body>
        </e:Envelope>
        """;

    public static IReadOnlyList<DiscoveredDevice> ParseProbeMatches(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (XmlException) { return []; }

        var result = new List<DiscoveredDevice>();
        foreach (var match in doc.Descendants().Where(e => e.Name.LocalName == "ProbeMatch"))
        {
            var urls = Words(Child(match, "XAddrs"))
                .Select(a => Uri.TryCreate(a, UriKind.Absolute, out var u) ? u : null)
                .OfType<Uri>()
                .ToList();
            var url = urls.FirstOrDefault(u => u.HostNameType == UriHostNameType.IPv4) ?? urls.FirstOrDefault();
            if (url is null) continue;

            var scopes = Words(Child(match, "Scopes"));
            result.Add(new DiscoveredDevice(url.Host, url.ToString(), Scope(scopes, "name"), Scope(scopes, "hardware")));
        }
        return result;
    }

    public static async Task<IReadOnlyList<DiscoveredDevice>> ProbeAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var payload = Encoding.UTF8.GetBytes(BuildProbe(Guid.NewGuid()));
        var found = new ConcurrentDictionary<string, DiscoveredDevice>();
        await Task.WhenAll(LocalIPv4Addresses().Select(local => ProbeFromAsync(local, payload, timeout, found, ct)));
        return found.Values.OrderBy(d => SortKey(d.Host)).ToList();
    }

    static async Task ProbeFromAsync(IPAddress local, byte[] payload, TimeSpan timeout,
        ConcurrentDictionary<string, DiscoveredDevice> found, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            // UDP may drop the first datagram; send twice.
            await udp.SendAsync(payload, MulticastEndpoint, cts.Token);
            await Task.Delay(100, cts.Token);
            await udp.SendAsync(payload, MulticastEndpoint, cts.Token);
            while (true)
            {
                var received = await udp.ReceiveAsync(cts.Token);
                foreach (var device in ParseProbeMatches(Encoding.UTF8.GetString(received.Buffer)))
                    found.TryAdd(device.Host, device);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
    }

    static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.SupportsMulticast)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

    static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    static string[] Words(string? value) =>
        (value ?? "").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

    static string? Scope(IEnumerable<string> scopes, string key)
    {
        var prefix = ScopePrefix + key + "/";
        return scopes
            .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(s => Uri.UnescapeDataString(s[prefix.Length..]))
            .FirstOrDefault();
    }

    static uint SortKey(string host) =>
        IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            ? (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(ip.GetAddressBytes()))
            : uint.MaxValue;
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Core.Tests --filter FullyQualifiedName~WsDiscoveryTests`
Expected: PASS (5 tests). Windows Firewall may prompt the first time; allowing it is the user's call — the timeout test passes either way.

- [ ] **Step 5: Commit**

```bash
git add src/CamaraWin.Core/Onvif tests/CamaraWin.Core.Tests/WsDiscoveryTests.cs
git commit -m "feat(core): ONVIF WS-Discovery probe and parser

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: ONVIF client (device info, profiles, stream URIs) and brand inference

**Files:**
- Create: `src/CamaraWin.Core/Onvif/OnvifClient.cs`, `src/CamaraWin.Core/Onvif/BrandInference.cs`
- Test: `tests/CamaraWin.Core.Tests/OnvifSamples.cs`, `tests/CamaraWin.Core.Tests/OnvifClientTests.cs`, `tests/CamaraWin.Core.Tests/BrandInferenceTests.cs`

**Interfaces:**
- Consumes: `Brand` (Task 1).
- Produces (namespace `CamaraWin.Core.Onvif`): `sealed record OnvifDeviceInfo(string Manufacturer, string Model)`; `sealed record OnvifProfile(string Token, int Width, int Height)`; `class OnvifException(string message) : Exception`; `sealed class OnvifAuthException(string message) : OnvifException`; `sealed class OnvifClient(HttpClient http, Uri deviceServiceUrl, string user, string password)` with `Task<OnvifDeviceInfo> GetDeviceInformationAsync(CancellationToken ct = default)`, `Task<(string Main, string Sub)> ResolveStreamUrisAsync(CancellationToken ct = default)`, statics `HttpClient CreateHttpClient(string user, string password)`, `string BuildSecurityHeader(string user, string password, byte[] nonce, DateTime createdUtc)`, `OnvifDeviceInfo ParseDeviceInformation(string xml)`, `Uri? ParseMediaXAddr(string xml)`, `IReadOnlyList<OnvifProfile> ParseProfiles(string xml)`, `string ParseStreamUri(string xml)`; `static Brand BrandInference.FromManufacturer(string? manufacturer)`.

- [ ] **Step 1: Write sample responses** — `tests/CamaraWin.Core.Tests/OnvifSamples.cs`

```csharp
namespace CamaraWin.Core.Tests;

static class OnvifSamples
{
    const string Head = """<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:trt="http://www.onvif.org/ver10/media/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema"><s:Body>""";
    const string Tail = "</s:Body></s:Envelope>";

    public const string DeviceInformation = Head + """
        <tds:GetDeviceInformationResponse><tds:Manufacturer>tp-link</tds:Manufacturer><tds:Model>C200</tds:Model>
        <tds:FirmwareVersion>1.3.6</tds:FirmwareVersion><tds:SerialNumber>x</tds:SerialNumber><tds:HardwareId>2.0</tds:HardwareId>
        </tds:GetDeviceInformationResponse>
        """ + Tail;

    public const string Capabilities = Head + """
        <tds:GetCapabilitiesResponse><tds:Capabilities><tt:Media><tt:XAddr>http://192.168.1.20:2020/onvif/service</tt:XAddr>
        <tt:StreamingCapabilities><tt:RTPMulticast>false</tt:RTPMulticast></tt:StreamingCapabilities></tt:Media></tds:Capabilities>
        </tds:GetCapabilitiesResponse>
        """ + Tail;

    public const string Profiles = Head + """
        <trt:GetProfilesResponse>
          <trt:Profiles token="profile_1" fixed="true"><tt:Name>mainStream</tt:Name>
            <tt:VideoSourceConfiguration token="vsc"><tt:Bounds x="0" y="0" width="1920" height="1080"/></tt:VideoSourceConfiguration>
            <tt:VideoEncoderConfiguration token="main"><tt:Encoding>H264</tt:Encoding>
              <tt:Resolution><tt:Width>1920</tt:Width><tt:Height>1080</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration>
          </trt:Profiles>
          <trt:Profiles token="profile_2" fixed="true"><tt:Name>minorStream</tt:Name>
            <tt:VideoEncoderConfiguration token="minor"><tt:Encoding>H264</tt:Encoding>
              <tt:Resolution><tt:Width>640</tt:Width><tt:Height>360</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration>
          </trt:Profiles>
        </trt:GetProfilesResponse>
        """ + Tail;

    public static string StreamUri(string uri) => Head + $"""
        <trt:GetStreamUriResponse><trt:MediaUri><tt:Uri>{uri}</tt:Uri><tt:InvalidAfterConnect>false</tt:InvalidAfterConnect>
        <tt:InvalidAfterReboot>false</tt:InvalidAfterReboot><tt:Timeout>PT0S</tt:Timeout></trt:MediaUri></trt:GetStreamUriResponse>
        """ + Tail;

    public const string NotAuthorizedFault = """
        <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:ter="http://www.onvif.org/ver10/error"><s:Body><s:Fault>
        <s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value>ter:NotAuthorized</s:Value></s:Subcode></s:Code>
        <s:Reason><s:Text xml:lang="en">Sender not Authorized</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>
        """;
}
```

- [ ] **Step 2: Write the failing tests**

`tests/CamaraWin.Core.Tests/OnvifClientTests.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CamaraWin.Core.Onvif;

namespace CamaraWin.Core.Tests;

public class OnvifClientTests
{
    sealed class FakeHandler(Func<Uri, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<Uri> Urls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Urls.Add(request.RequestUri!);
            var (code, xml) = respond(request.RequestUri!, body);
            return new HttpResponseMessage(code) { Content = new StringContent(xml) };
        }
    }

    static readonly Uri Device = new("http://192.168.1.20:2020/onvif/device_service");

    [Fact]
    public void Parses_device_information() =>
        Assert.Equal(new OnvifDeviceInfo("tp-link", "C200"), OnvifClient.ParseDeviceInformation(OnvifSamples.DeviceInformation));

    [Fact]
    public void Parses_media_xaddr() =>
        Assert.Equal(new Uri("http://192.168.1.20:2020/onvif/service"), OnvifClient.ParseMediaXAddr(OnvifSamples.Capabilities));

    [Fact]
    public void Parses_profiles_with_encoder_resolution() =>
        Assert.Equal(
            new[] { new OnvifProfile("profile_1", 1920, 1080), new OnvifProfile("profile_2", 640, 360) },
            OnvifClient.ParseProfiles(OnvifSamples.Profiles));

    [Fact]
    public void Parses_stream_uri() =>
        Assert.Equal("rtsp://192.168.1.20:554/stream1", OnvifClient.ParseStreamUri(OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream1")));

    [Fact]
    public void Stream_uri_missing_throws() =>
        Assert.Throws<OnvifException>(() => OnvifClient.ParseStreamUri(OnvifSamples.DeviceInformation));

    [Fact]
    public void Security_header_has_password_digest()
    {
        var nonce = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var created = new DateTime(2026, 9, 26, 18, 0, 0, DateTimeKind.Utc);
        var header = OnvifClient.BuildSecurityHeader("admin", "secret", nonce, created);

        var expected = Convert.ToBase64String(SHA1.HashData(
            [.. nonce, .. Encoding.UTF8.GetBytes("2026-09-26T18:00:00.000Z"), .. Encoding.UTF8.GetBytes("secret")]));
        Assert.Contains("<Username>admin</Username>", header);
        Assert.Contains(expected, header);
        Assert.Contains(Convert.ToBase64String(nonce), header);
        Assert.Contains("2026-09-26T18:00:00.000Z", header);
        Assert.DoesNotContain("secret", header);
    }

    [Fact]
    public async Task Resolves_main_and_sub_via_media_service()
    {
        var handler = new FakeHandler((_, body) => body switch
        {
            _ when body.Contains("GetCapabilities") => (HttpStatusCode.OK, OnvifSamples.Capabilities),
            _ when body.Contains("GetProfiles") => (HttpStatusCode.OK, OnvifSamples.Profiles),
            _ when body.Contains("profile_1") => (HttpStatusCode.OK, OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream1")),
            _ when body.Contains("profile_2") => (HttpStatusCode.OK, OnvifSamples.StreamUri("rtsp://192.168.1.20:554/stream2")),
            _ => (HttpStatusCode.BadRequest, ""),
        });
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "secret");

        var (main, sub) = await client.ResolveStreamUrisAsync();

        Assert.Equal("rtsp://192.168.1.20:554/stream1", main);
        Assert.Equal("rtsp://192.168.1.20:554/stream2", sub);
        Assert.Equal(Device, handler.Urls[0]);
        Assert.All(handler.Urls.Skip(1), u => Assert.Equal("/onvif/service", u.AbsolutePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Auth_failures_throw_OnvifAuthException(HttpStatusCode code)
    {
        var handler = new FakeHandler((_, _) => (code, OnvifSamples.NotAuthorizedFault));
        var client = new OnvifClient(new HttpClient(handler), Device, "admin", "wrong");
        await Assert.ThrowsAsync<OnvifAuthException>(() => client.GetDeviceInformationAsync());
    }
}
```

`tests/CamaraWin.Core.Tests/BrandInferenceTests.cs`:

```csharp
using CamaraWin.Core;
using CamaraWin.Core.Onvif;

namespace CamaraWin.Core.Tests;

public class BrandInferenceTests
{
    [Theory]
    [InlineData("tp-link", Brand.Tapo)]
    [InlineData("TP-LINK", Brand.Tapo)]
    [InlineData("Tapo", Brand.Tapo)]
    [InlineData("Dahua", Brand.Imou)]
    [InlineData("IMOU", Brand.Imou)]
    [InlineData("Hikvision", Brand.Custom)]
    [InlineData(null, Brand.Custom)]
    public void Infers_brand(string? manufacturer, Brand expected) =>
        Assert.Equal(expected, BrandInference.FromManufacturer(manufacturer));
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Core.Tests --filter "FullyQualifiedName~Onvif|FullyQualifiedName~BrandInference"`
Expected: build FAILS (`OnvifClient` not found).

- [ ] **Step 4: Implement**

`src/CamaraWin.Core/Onvif/BrandInference.cs`:

```csharp
namespace CamaraWin.Core.Onvif;

public static class BrandInference
{
    public static Brand FromManufacturer(string? manufacturer)
    {
        var m = manufacturer ?? "";
        if (ContainsAny(m, "tp-link", "tplink", "tapo")) return Brand.Tapo;
        if (ContainsAny(m, "dahua", "imou")) return Brand.Imou;
        return Brand.Custom;
    }

    static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
}
```

`src/CamaraWin.Core/Onvif/OnvifClient.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CamaraWin.Core.Onvif;

public sealed record OnvifDeviceInfo(string Manufacturer, string Model);

public sealed record OnvifProfile(string Token, int Width, int Height);

public class OnvifException(string message) : Exception(message);

public sealed class OnvifAuthException(string message) : OnvifException(message);

public sealed class OnvifClient(HttpClient http, Uri deviceServiceUrl, string user, string password)
{
    const string DeviceNs = "http://www.onvif.org/ver10/device/wsdl";
    const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";
    const string SchemaNs = "http://www.onvif.org/ver10/schema";

    /// <summary>HttpClient that also answers HTTP Digest challenges (some Dahua/Imou firmwares).</summary>
    public static HttpClient CreateHttpClient(string user, string password) =>
        new(new HttpClientHandler { Credentials = new NetworkCredential(user, password) })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

    public async Task<OnvifDeviceInfo> GetDeviceInformationAsync(CancellationToken ct = default) =>
        ParseDeviceInformation(await SendAsync(deviceServiceUrl, $"<GetDeviceInformation xmlns=\"{DeviceNs}\"/>", ct));

    public async Task<(string Main, string Sub)> ResolveStreamUrisAsync(CancellationToken ct = default)
    {
        var media = await GetMediaServiceUrlAsync(ct);
        var profiles = ParseProfiles(await SendAsync(media, $"<GetProfiles xmlns=\"{MediaNs}\"/>", ct));
        if (profiles.Count == 0) throw new OnvifException("La cámara no tiene perfiles de vídeo.");

        var ordered = profiles.OrderByDescending(p => p.Width * p.Height).ToList();
        var main = await GetStreamUriAsync(media, ordered[0].Token, ct);
        var sub = ordered.Count > 1 ? await GetStreamUriAsync(media, ordered[^1].Token, ct) : main;
        return (main, sub);
    }

    async Task<Uri> GetMediaServiceUrlAsync(CancellationToken ct)
    {
        try
        {
            var xml = await SendAsync(deviceServiceUrl,
                $"<GetCapabilities xmlns=\"{DeviceNs}\"><Category>Media</Category></GetCapabilities>", ct);
            return ParseMediaXAddr(xml) ?? deviceServiceUrl;
        }
        catch (OnvifException ex) when (ex is not OnvifAuthException)
        {
            return deviceServiceUrl;
        }
    }

    async Task<string> GetStreamUriAsync(Uri media, string token, CancellationToken ct) =>
        ParseStreamUri(await SendAsync(media, $"""
            <GetStreamUri xmlns="{MediaNs}"><StreamSetup><Stream xmlns="{SchemaNs}">RTP-Unicast</Stream><Transport xmlns="{SchemaNs}"><Protocol>RTSP</Protocol></Transport></StreamSetup><ProfileToken>{SecurityElement.Escape(token)}</ProfileToken></GetStreamUri>
            """, ct));

    async Task<string> SendAsync(Uri url, string body, CancellationToken ct)
    {
        var header = BuildSecurityHeader(user, password, RandomNumberGenerator.GetBytes(16), DateTime.UtcNow);
        var envelope = $"""<?xml version="1.0" encoding="utf-8"?><s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Header>{header}</s:Header><s:Body>{body}</s:Body></s:Envelope>""";
        using var content = new StringContent(envelope, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/soap+xml; charset=utf-8");

        using var response = await http.PostAsync(url, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || text.Contains("NotAuthorized", StringComparison.Ordinal))
            throw new OnvifAuthException("Usuario o contraseña ONVIF incorrectos.");
        if (!response.IsSuccessStatusCode)
            throw new OnvifException($"ONVIF respondió {(int)response.StatusCode}.");
        return text;
    }

    public static string BuildSecurityHeader(string user, string password, byte[] nonce, DateTime createdUtc)
    {
        var created = createdUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var digest = Convert.ToBase64String(SHA1.HashData(
            [.. nonce, .. Encoding.UTF8.GetBytes(created), .. Encoding.UTF8.GetBytes(password)]));
        return $"""<Security s:mustUnderstand="1" xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"><UsernameToken><Username>{SecurityElement.Escape(user)}</Username><Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</Password><Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{Convert.ToBase64String(nonce)}</Nonce><Created xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">{created}</Created></UsernameToken></Security>""";
    }

    public static OnvifDeviceInfo ParseDeviceInformation(string xml)
    {
        var doc = XDocument.Parse(xml);
        return new OnvifDeviceInfo(Find(doc, "Manufacturer")?.Value.Trim() ?? "", Find(doc, "Model")?.Value.Trim() ?? "");
    }

    public static Uri? ParseMediaXAddr(string xml)
    {
        var media = Find(XDocument.Parse(xml), "Media");
        var address = media is null ? null : Find(media, "XAddr")?.Value.Trim();
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
    }

    public static IReadOnlyList<OnvifProfile> ParseProfiles(string xml) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName == "Profiles")
            .Select(p =>
            {
                var encoder = Find(p, "VideoEncoderConfiguration");
                var resolution = encoder is null ? null : Find(encoder, "Resolution");
                return new OnvifProfile((string?)p.Attribute("token") ?? "", Int(resolution, "Width"), Int(resolution, "Height"));
            })
            .Where(p => p.Token.Length > 0)
            .ToList();

    public static string ParseStreamUri(string xml)
    {
        var media = Find(XDocument.Parse(xml), "MediaUri");
        var uri = media is null ? null : Find(media, "Uri")?.Value.Trim();
        return string.IsNullOrEmpty(uri) ? throw new OnvifException("GetStreamUri no devolvió ninguna URL.") : uri;
    }

    static XElement? Find(XContainer container, string localName) =>
        container.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    static int Int(XElement? parent, string localName) =>
        int.TryParse(parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value, out var value) ? value : 0;
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Core.Tests`
Expected: PASS (all Core tests).

- [ ] **Step 6: Commit**

```bash
git add src/CamaraWin.Core/Onvif tests/CamaraWin.Core.Tests
git commit -m "feat(core): ONVIF client for device info and stream URIs, brand inference

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: FFmpeg setup — download script, Media project, loader and errors

**Files:**
- Create: `tools/get-ffmpeg.ps1`, `Directory.Build.targets`, `src/CamaraWin.Media/CamaraWin.Media.csproj`, `src/CamaraWin.Media/FFmpegLoader.cs`, `src/CamaraWin.Media/FFmpegException.cs`
- Create: `tests/CamaraWin.Media.Tests/CamaraWin.Media.Tests.csproj`, `tests/CamaraWin.Media.Tests/FFmpegLoaderTests.cs`
- Modify: `.gitignore` (add `tools/bin/`)

**Interfaces:**
- Produces: `static void FFmpegLoader.Initialize(string? directory = null)`, `static string FFmpegLoader.Version`; `sealed class FFmpegException : Exception { int ErrorCode; bool IsAuthError; static string Describe(int error); static int ThrowIfError(int result, string operation); }`. Namespace `CamaraWin.Media`.

- [ ] **Step 1: Download script** — `tools/get-ffmpeg.ps1`

```powershell
# Downloads FFmpeg 9.0 LGPL shared build (BtbN) into <repo>/ffmpeg/bin.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'ffmpeg'
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip'
$zip = Join-Path $env:TEMP 'camarawin-ffmpeg.zip'
$tmp = Join-Path $env:TEMP 'camarawin-ffmpeg'

Invoke-WebRequest $url -OutFile $zip
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive $zip $tmp
$bin = Get-ChildItem $tmp -Recurse -Directory -Filter bin | Select-Object -First 1
New-Item -ItemType Directory -Force (Join-Path $dest 'bin') | Out-Null
Copy-Item (Join-Path $bin.FullName '*.dll') (Join-Path $dest 'bin') -Force
$license = Get-ChildItem $tmp -Recurse -Filter 'LICENSE*' | Select-Object -First 1
if ($license) { Copy-Item $license.FullName (Join-Path $dest 'LICENSE.txt') -Force }
Remove-Item $zip, $tmp -Recurse -Force
Write-Host "FFmpeg DLLs in $dest\bin"
```

- [ ] **Step 2: Ask the user, then run it**

Tell the user: "Voy a descargar `ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip` (≈ 60–90 MB) desde github.com/BtbN/FFmpeg-Builds. ¿Adelante?" Only after a yes:

```bash
pwsh -File tools/get-ffmpeg.ps1
ls ffmpeg/bin
```

Expected: `avcodec-63.dll avdevice-63.dll avfilter-12.dll avformat-63.dll avutil-61.dll swresample-7.dll swscale-10.dll`. If the majors differ, stop and report: FFmpeg.AutoGen 9.0.1.1 needs exactly these.

- [ ] **Step 3: Build plumbing**

`Directory.Build.targets`:

```xml
<Project>
  <ItemGroup Condition="'$(CopyFFmpeg)' == 'true'">
    <None Include="$(MSBuildThisFileDirectory)ffmpeg\bin\*.dll"
          Link="ffmpeg\%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest"
          Visible="false" />
  </ItemGroup>
</Project>
```

Append to `.gitignore`:

```
tools/bin/
```

Create projects:

```bash
dotnet new classlib -n CamaraWin.Media -o src/CamaraWin.Media
dotnet new xunit -n CamaraWin.Media.Tests -o tests/CamaraWin.Media.Tests
rm src/CamaraWin.Media/Class1.cs tests/CamaraWin.Media.Tests/UnitTest1.cs
sed -i '/<TargetFramework>/d' src/CamaraWin.Media/CamaraWin.Media.csproj tests/CamaraWin.Media.Tests/CamaraWin.Media.Tests.csproj
dotnet sln CamaraWin.slnx add src/CamaraWin.Media tests/CamaraWin.Media.Tests
dotnet add src/CamaraWin.Media package FFmpeg.AutoGen --version 9.0.1.1
dotnet add tests/CamaraWin.Media.Tests package Xunit.SkippableFact
dotnet add tests/CamaraWin.Media.Tests reference src/CamaraWin.Media
```

Add to the first `<PropertyGroup>` of `src/CamaraWin.Media/CamaraWin.Media.csproj`:

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

Add to the first `<PropertyGroup>` of `tests/CamaraWin.Media.Tests/CamaraWin.Media.Tests.csproj`:

```xml
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <CopyFFmpeg>true</CopyFFmpeg>
```

- [ ] **Step 4: Write the failing tests** — `tests/CamaraWin.Media.Tests/FFmpegLoaderTests.cs`

```csharp
using CamaraWin.Media;
using FFmpeg.AutoGen;

namespace CamaraWin.Media.Tests;

public class FFmpegLoaderTests
{
    public FFmpegLoaderTests() => FFmpegLoader.Initialize();

    [Fact]
    public void Loaded_avcodec_major_matches_bindings() =>
        Assert.Equal(ffmpeg.LibraryVersionMap["avcodec"], (int)(ffmpeg.avcodec_version() >> 16));

    [Fact]
    public void Version_is_reported() =>
        Assert.False(string.IsNullOrWhiteSpace(FFmpegLoader.Version));

    [Fact]
    public void Describe_translates_error_codes() =>
        Assert.Contains("End of file", FFmpegException.Describe(ffmpeg.AVERROR_EOF));

    [Fact]
    public void Unauthorized_is_an_auth_error() =>
        Assert.True(new FFmpegException(ffmpeg.AVERROR_HTTP_UNAUTHORIZED, "open").IsAuthError);

    [Fact]
    public void ThrowIfError_passes_through_non_negative() =>
        Assert.Equal(3, FFmpegException.ThrowIfError(3, "x"));

    [Fact]
    public void ThrowIfError_throws_on_negative() =>
        Assert.Throws<FFmpegException>(() => FFmpegException.ThrowIfError(ffmpeg.AVERROR_EOF, "read"));
}
```

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Media.Tests`
Expected: build FAILS (`FFmpegLoader` not found).

- [ ] **Step 6: Implement**

`src/CamaraWin.Media/FFmpegLoader.cs`:

```csharp
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public static class FFmpegLoader
{
    static readonly object Gate = new();
    static bool _loaded;

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static void Initialize(string? directory = null)
    {
        lock (Gate)
        {
            if (_loaded) return;
            var dir = directory ?? DefaultDirectory;
            var avcodec = Path.Combine(dir, $"avcodec-{ffmpeg.LibraryVersionMap["avcodec"]}.dll");
            if (!File.Exists(avcodec))
                throw new FileNotFoundException(
                    $"No se encuentran las DLLs de FFmpeg en '{dir}'. Ejecuta tools/get-ffmpeg.ps1 y recompila.", avcodec);

            ffmpeg.RootPath = dir;
            DynamicallyLoadedBindings.Initialize();
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            _loaded = true;
        }
    }

    public static string Version => ffmpeg.av_version_info();
}
```

`src/CamaraWin.Media/FFmpegException.cs`:

```csharp
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public sealed unsafe class FFmpegException(int errorCode, string operation)
    : Exception($"{operation}: {Describe(errorCode)} ({errorCode})")
{
    const int AverrorEacces = -13;

    public int ErrorCode { get; } = errorCode;

    public bool IsAuthError =>
        ErrorCode == ffmpeg.AVERROR_HTTP_UNAUTHORIZED
        || ErrorCode == ffmpeg.AVERROR_HTTP_FORBIDDEN
        || ErrorCode == AverrorEacces;

    public static string Describe(int error)
    {
        const int size = 256;
        var buffer = stackalloc byte[size];
        ffmpeg.av_strerror(error, buffer, size);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"error {error}";
    }

    public static int ThrowIfError(int result, string operation) =>
        result < 0 ? throw new FFmpegException(result, operation) : result;
}
```

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Media.Tests`
Expected: PASS (6 tests). If `ffmpeg.AV_LOG_ERROR` does not exist in the bindings, replace it with `16` (its value) and note that in the commit.

- [ ] **Step 8: Commit**

```bash
git add .gitignore Directory.Build.targets tools/get-ffmpeg.ps1 CamaraWin.slnx src/CamaraWin.Media tests/CamaraWin.Media.Tests
git commit -m "feat(media): FFmpeg 9 loader, error helpers and download script

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Frame mailbox and fit-size geometry

**Files:**
- Create: `src/CamaraWin.Media/FrameMailbox.cs`, `src/CamaraWin.Media/FrameGeometry.cs`
- Test: `tests/CamaraWin.Media.Tests/FrameMailboxTests.cs`, `tests/CamaraWin.Media.Tests/FrameGeometryTests.cs`

**Interfaces:**
- Produces: `sealed class VideoFrame(int width, int height) { int Width; int Height; int Stride; byte[] Data; }` (BGRA, stride = width*4); `sealed class FrameMailbox { long Sequence; VideoFrame Rent(int width, int height); void Publish(VideoFrame frame); bool TryRead(ref long lastSequence, Action<VideoFrame> read); }`; `static (int Width, int Height) FrameGeometry.FitSize(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)`.

- [ ] **Step 1: Write the failing tests**

`tests/CamaraWin.Media.Tests/FrameMailboxTests.cs`:

```csharp
using CamaraWin.Media;

namespace CamaraWin.Media.Tests;

public class FrameMailboxTests
{
    [Fact]
    public void Frame_has_bgra_stride()
    {
        var f = new VideoFrame(10, 3);
        Assert.Equal(40, f.Stride);
        Assert.Equal(120, f.Data.Length);
    }

    [Fact]
    public void TryRead_returns_false_when_nothing_published()
    {
        long seq = 0;
        Assert.False(new FrameMailbox().TryRead(ref seq, _ => { }));
    }

    [Fact]
    public void Published_frame_is_read_once()
    {
        var mailbox = new FrameMailbox();
        var frame = mailbox.Rent(4, 2);
        frame.Data[0] = 7;
        mailbox.Publish(frame);

        long seq = 0;
        byte seen = 0;
        Assert.True(mailbox.TryRead(ref seq, f => seen = f.Data[0]));
        Assert.Equal(7, seen);
        Assert.False(mailbox.TryRead(ref seq, _ => { }));
    }

    [Fact]
    public void Latest_frame_wins()
    {
        var mailbox = new FrameMailbox();
        var first = mailbox.Rent(4, 2);
        first.Data[0] = 1;
        mailbox.Publish(first);
        var second = mailbox.Rent(4, 2);
        second.Data[0] = 2;
        mailbox.Publish(second);

        long seq = 0;
        byte seen = 0;
        mailbox.TryRead(ref seq, f => seen = f.Data[0]);
        Assert.Equal(2, seen);
        Assert.Equal(2, mailbox.Sequence);
    }

    [Fact]
    public void Rent_reuses_the_displaced_buffer_when_size_matches()
    {
        var mailbox = new FrameMailbox();
        var a = mailbox.Rent(4, 2);
        mailbox.Publish(a);
        var b = mailbox.Rent(4, 2);
        mailbox.Publish(b);
        Assert.Same(a, mailbox.Rent(4, 2));
    }

    [Fact]
    public void Rent_allocates_when_size_changes()
    {
        var mailbox = new FrameMailbox();
        var a = mailbox.Rent(4, 2);
        mailbox.Publish(a);
        mailbox.Publish(mailbox.Rent(4, 2));
        var c = mailbox.Rent(8, 4);
        Assert.NotSame(a, c);
        Assert.Equal(32, c.Stride);
    }
}
```

`tests/CamaraWin.Media.Tests/FrameGeometryTests.cs`:

```csharp
using CamaraWin.Media;

namespace CamaraWin.Media.Tests;

public class FrameGeometryTests
{
    [Theory]
    [InlineData(640, 360, 0, 0, 640, 360)]
    [InlineData(640, 360, 320, 320, 320, 180)]
    [InlineData(1920, 1080, 1000, 1000, 1000, 562)]
    [InlineData(640, 360, 1280, 720, 640, 360)]
    [InlineData(640, 360, 641, 100, 178, 100)]
    public void FitSize_keeps_aspect_never_upscales_and_is_even(int sw, int sh, int bw, int bh, int w, int h) =>
        Assert.Equal((w, h), FrameGeometry.FitSize(sw, sh, bw, bh));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Media.Tests --filter "FullyQualifiedName~FrameMailbox|FullyQualifiedName~FrameGeometry"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/CamaraWin.Media/FrameMailbox.cs`:

```csharp
namespace CamaraWin.Media;

/// <summary>A BGRA image. Stride is always Width * 4.</summary>
public sealed class VideoFrame(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = width * 4;
    public byte[] Data { get; } = new byte[width * 4 * height];
}

/// <summary>
/// Single-slot, latest-wins handoff between the decode thread and the UI.
/// Two buffers rotate: the producer fills the one it rented while the consumer reads the latest.
/// Stale frames are overwritten, never queued, so display latency never accumulates.
/// </summary>
public sealed class FrameMailbox
{
    readonly object _gate = new();
    VideoFrame? _latest;
    VideoFrame? _spare;
    long _sequence;

    public long Sequence => Interlocked.Read(ref _sequence);

    public VideoFrame Rent(int width, int height)
    {
        lock (_gate)
        {
            var frame = _spare;
            _spare = null;
            return frame is not null && frame.Width == width && frame.Height == height
                ? frame
                : new VideoFrame(width, height);
        }
    }

    public void Publish(VideoFrame frame)
    {
        lock (_gate)
        {
            _spare = _latest;
            _latest = frame;
            Interlocked.Increment(ref _sequence);
        }
    }

    public bool TryRead(ref long lastSequence, Action<VideoFrame> read)
    {
        lock (_gate)
        {
            if (_latest is null || _sequence == lastSequence) return false;
            read(_latest);
            lastSequence = _sequence;
            return true;
        }
    }
}
```

`src/CamaraWin.Media/FrameGeometry.cs`:

```csharp
namespace CamaraWin.Media;

public static class FrameGeometry
{
    /// <summary>Largest even size with the source aspect ratio that fits the box. Never upscales. Box of 0 = native.</summary>
    public static (int Width, int Height) FitSize(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)
    {
        if (boxWidth <= 0 || boxHeight <= 0 || (boxWidth >= sourceWidth && boxHeight >= sourceHeight))
            return (sourceWidth, sourceHeight);
        var scale = Math.Min(boxWidth / (double)sourceWidth, boxHeight / (double)sourceHeight);
        return (Even(sourceWidth * scale), Even(sourceHeight * scale));
    }

    static int Even(double value) => Math.Max(2, (int)Math.Round(value) & ~1);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Media.Tests --filter "FullyQualifiedName~FrameMailbox|FullyQualifiedName~FrameGeometry"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CamaraWin.Media tests/CamaraWin.Media.Tests
git commit -m "feat(media): latest-wins frame mailbox and fit-size geometry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: RTSP test server and StreamSession (connect, decode, scale, states, reconnect)

**Files:**
- Create: `tools/get-mediamtx.ps1`, `tests/CamaraWin.Media.Tests/Rtsp/RtspTestServer.cs`, `tests/CamaraWin.Media.Tests/TestUtil.cs`, `src/CamaraWin.Media/StreamSession.cs`
- Test: `tests/CamaraWin.Media.Tests/StreamSessionTests.cs`

**Interfaces:**
- Consumes: `FFmpegException`, `FrameMailbox`, `FrameGeometry` (Tasks 6–7).
- Produces: `enum SessionState { Idle, Connecting, Playing, Reconnecting, AuthFailed, Stopped }`; `sealed unsafe class StreamSession(string url, bool useUdp = false, bool decode = true) : IDisposable` with `FrameMailbox Mailbox`, `SessionState State`, `string? LastError`, `event Action<SessionState>? StateChanged` (raised on the session thread), `void SetTargetSize(int width, int height)`, `void Start()`, `void RequestStop()`, `void Stop()`, `void Dispose()`. Tasks 9–10 add recording and snapshot members to this class.
- Test fixture: `RtspTestServer` with `string? SkipReason`, `string Url(string path, string? user = null, string? password = null)`, `void RestartServer()`, constants `SecureUser = "viewer"`, `SecurePassword = "p@ss:w/rd"`; collection name `"rtsp"`.

- [ ] **Step 1: mediamtx script** — `tools/get-mediamtx.ps1`

```powershell
# Downloads the latest mediamtx (MIT) for Windows into <repo>/tools/bin. Used only by tests.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'tools\bin'
$release = Invoke-RestMethod 'https://api.github.com/repos/bluenviron/mediamtx/releases/latest'
$asset = $release.assets | Where-Object name -like '*windows_amd64.zip' | Select-Object -First 1
$zip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip
New-Item -ItemType Directory -Force $dest | Out-Null
Expand-Archive $zip $dest -Force
Remove-Item $zip
Write-Host "mediamtx $($release.tag_name) in $dest"
```

Ask the user: "Para las pruebas de vídeo necesito mediamtx (servidor RTSP, licencia MIT, ≈ 15 MB, github.com/bluenviron/mediamtx). ¿Lo descargo?" Only after a yes:

```bash
pwsh -File tools/get-mediamtx.ps1
```

Expected: `tools/bin/mediamtx.exe` exists. The publisher uses `ffmpeg` from PATH (the user's gyan.dev 8.1 build has libx264).

- [ ] **Step 2: Test fixture** — `tests/CamaraWin.Media.Tests/Rtsp/RtspTestServer.cs`

```csharp
using System.Diagnostics;
using System.Net.Sockets;

namespace CamaraWin.Media.Tests.Rtsp;

[CollectionDefinition("rtsp")]
public sealed class RtspCollection : ICollectionFixture<RtspTestServer>;

/// <summary>mediamtx on :18554 with two looping 640x360 H.264 test streams: "open" (anonymous) and "secure" (viewer / p@ss:w/rd).</summary>
public sealed class RtspTestServer : IDisposable
{
    public const int Port = 18554;
    public const string SecureUser = "viewer";
    public const string SecurePassword = "p@ss:w/rd";

    const string Config = """
        logLevel: warn
        rtspAddress: :18554
        rtpAddress: :18000
        rtcpAddress: :18001
        rtmp: no
        hls: no
        webrtc: no
        srt: no
        api: no
        metrics: no
        pprof: no
        playback: no
        authInternalUsers:
          - user: any
            pass:
            ips: []
            permissions:
              - action: publish
              - action: read
                path: open
          - user: viewer
            pass: "p@ss:w/rd"
            ips: []
            permissions:
              - action: read
                path: secure
        paths:
          all_others:
        """;

    readonly string? _serverExe;
    readonly string? _ffmpegExe;
    readonly string _configPath = Path.Combine(Path.GetTempPath(), $"camarawin-mediamtx-{Guid.NewGuid()}.yml");
    readonly List<Process> _processes = [];

    public RtspTestServer()
    {
        _serverExe = Path.Combine(TestUtil.RepoRoot, "tools", "bin", "mediamtx.exe");
        _ffmpegExe = TestUtil.FindOnPath("ffmpeg.exe");
        if (!File.Exists(_serverExe)) { SkipReason = "mediamtx no encontrado: ejecuta tools/get-mediamtx.ps1"; return; }
        if (_ffmpegExe is null) { SkipReason = "ffmpeg.exe (con libx264) no está en el PATH"; return; }
        File.WriteAllText(_configPath, Config);
        StartAll();
    }

    public string? SkipReason { get; }

    public string Url(string path, string? user = null, string? password = null) =>
        user is null
            ? $"rtsp://127.0.0.1:{Port}/{path}"
            : $"rtsp://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password ?? "")}@127.0.0.1:{Port}/{path}";

    public void RestartServer()
    {
        KillAll();
        StartAll();
    }

    void StartAll()
    {
        _processes.Add(Launch(_serverExe!, $"\"{_configPath}\""));
        WaitForPort();
        foreach (var path in new[] { "open", "secure" })
            _processes.Add(Launch(_ffmpegExe!,
                "-hide_banner -loglevel error -re -f lavfi -i testsrc2=size=640x360:rate=25 " +
                "-c:v libx264 -preset ultrafast -tune zerolatency -g 25 -pix_fmt yuv420p " +
                $"-f rtsp -rtsp_transport tcp rtsp://127.0.0.1:{Port}/{path}"));
        Thread.Sleep(2000);
    }

    static Process Launch(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true })!;

    static void WaitForPort()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect("127.0.0.1", Port);
                return;
            }
            catch (SocketException) { Thread.Sleep(100); }
        }
        throw new TimeoutException("mediamtx did not open its RTSP port");
    }

    void KillAll()
    {
        foreach (var p in _processes)
        {
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } }
            catch (InvalidOperationException) { }
            p.Dispose();
        }
        _processes.Clear();
    }

    public void Dispose()
    {
        KillAll();
        if (File.Exists(_configPath)) File.Delete(_configPath);
    }
}
```

`tests/CamaraWin.Media.Tests/TestUtil.cs`:

```csharp
namespace CamaraWin.Media.Tests;

static class TestUtil
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CamaraWin.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("CamaraWin.slnx not found above test output");
        }
    }

    public static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), exe))
            .FirstOrDefault(File.Exists);

    public static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }
}
```

- [ ] **Step 3: Write the failing tests** — `tests/CamaraWin.Media.Tests/StreamSessionTests.cs`

```csharp
using System.Diagnostics;
using CamaraWin.Media.Tests.Rtsp;
using Xunit.Abstractions;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class StreamSessionTests(RtspTestServer server, ITestOutputHelper output)
{
    static readonly TimeSpan Ten = TimeSpan.FromSeconds(10);

    StreamSession Open(string url)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(url);
    }

    [SkippableFact]
    public void Plays_and_publishes_native_size_frames()
    {
        using var session = Open(server.Url("open"));
        var watch = Stopwatch.StartNew();
        session.Start();

        Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 0, Ten), session.LastError);
        output.WriteLine($"First frame after {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.ElapsedMilliseconds < 2500, $"first frame took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(SessionState.Playing, session.State);

        long seq = 0;
        (int w, int h) size = default;
        session.Mailbox.TryRead(ref seq, f => size = (f.Width, f.Height));
        Assert.Equal((640, 360), size);
    }

    [SkippableFact]
    public void Scales_to_target_size()
    {
        using var session = Open(server.Url("open"));
        session.SetTargetSize(320, 320);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 2, Ten), session.LastError);

        long seq = 0;
        (int w, int h) size = default;
        session.Mailbox.TryRead(ref seq, f => size = (f.Width, f.Height));
        Assert.Equal((320, 180), size);
    }

    [SkippableFact]
    public void Authenticates_with_special_character_password()
    {
        using var session = Open(server.Url("secure", RtspTestServer.SecureUser, RtspTestServer.SecurePassword));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);
    }

    [SkippableFact]
    public void Wrong_password_ends_in_AuthFailed()
    {
        using var session = Open(server.Url("secure", RtspTestServer.SecureUser, "wrong"));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.AuthFailed, Ten),
            $"state={session.State} error={session.LastError}");
    }

    [SkippableFact]
    public void Unreachable_camera_goes_to_Reconnecting()
    {
        using var session = Open("rtsp://127.0.0.1:1/none");
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Reconnecting, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, session.Mailbox.Sequence);
    }

    [SkippableFact]
    public void Stop_is_fast_and_final()
    {
        using var session = Open(server.Url("open"));
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);

        var watch = Stopwatch.StartNew();
        session.Stop();
        Assert.True(watch.ElapsedMilliseconds < 2000, $"stop took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(SessionState.Stopped, session.State);
    }

    [SkippableFact]
    public void Reconnects_after_server_restart()
    {
        using var session = Open(server.Url("open"));
        var states = new List<SessionState>();
        session.StateChanged += s => { lock (states) states.Add(s); };
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, Ten), session.LastError);

        server.RestartServer();

        Assert.True(TestUtil.WaitFor(() =>
        {
            lock (states) return states.Contains(SessionState.Reconnecting) && session.State == SessionState.Playing;
        }, TimeSpan.FromSeconds(25)), $"state={session.State} error={session.LastError}");
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Media.Tests --filter FullyQualifiedName~StreamSessionTests`
Expected: build FAILS (`StreamSession` not found).

- [ ] **Step 5: Implement** — `src/CamaraWin.Media/StreamSession.cs`

```csharp
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public enum SessionState { Idle, Connecting, Playing, Reconnecting, AuthFailed, Stopped }

/// <summary>
/// One RTSP connection on its own thread. Decoding sessions push the newest frame (BGRA, scaled to
/// the target size) into <see cref="Mailbox"/>; there is no buffering, clock or queue on purpose.
/// </summary>
public sealed unsafe partial class StreamSession : IDisposable
{
    const int OpenTimeoutMs = 10_000;
    const int StallTimeoutMs = 5_000;
    const int MaxBackoffSeconds = 10;
    const int MaxConsecutiveDecodeErrors = 100;

    readonly string _url;
    readonly bool _useUdp;
    readonly bool _decode;
    readonly AVIOInterruptCB_callback _interrupt;
    readonly AVCodecContext_get_format _getFormat;
    readonly ManualResetEventSlim _stopSignal = new(false);
    readonly object _snapshotLock = new();

    Thread? _thread;
    volatile bool _stopping;
    volatile SessionState _state = SessionState.Idle;
    long _deadline;
    volatile int _targetWidth;
    volatile int _targetHeight;
    bool _forceSoftware;
    int _decodeErrors;
    SwsContext* _sws;
    AVFrame* _lastFrame; // native-resolution software frame for snapshots; guarded by _snapshotLock

    public StreamSession(string url, bool useUdp = false, bool decode = true)
    {
        _url = url;
        _useUdp = useUdp;
        _decode = decode;
        // Delegates are kept in fields so the GC never collects them while FFmpeg holds the pointer.
        _interrupt = _ => _stopping || Environment.TickCount64 > Interlocked.Read(ref _deadline) ? 1 : 0;
        _getFormat = GetFormat;
    }

    public FrameMailbox Mailbox { get; } = new();
    public SessionState State => _state;
    public string? LastError { get; private set; }
    public event Action<SessionState>? StateChanged;

    /// <summary>Box the decoded image must fit in, in device pixels. 0 = native size.</summary>
    public void SetTargetSize(int width, int height)
    {
        _targetWidth = Math.Max(0, width);
        _targetHeight = Math.Max(0, height);
    }

    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("Session already started.");
        _thread = new Thread(Run) { IsBackground = true, Name = "StreamSession" };
        _thread.Start();
    }

    public void RequestStop()
    {
        _stopping = true;
        _stopSignal.Set();
    }

    public void Stop()
    {
        RequestStop();
        if (_thread is { } thread && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(3));
    }

    public void Dispose() => Stop();

    void Run()
    {
        AVBufferRef* hwDevice = null;
        if (_decode && ffmpeg.av_hwdevice_ctx_create(&hwDevice, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) < 0)
            hwDevice = null;
        var backoff = 1;
        try
        {
            SetState(SessionState.Connecting);
            while (!_stopping)
            {
                var reachedPlaying = false;
                try
                {
                    PlayOnce(hwDevice, ref reachedPlaying);
                }
                catch (FFmpegException ex) when (ex.IsAuthError)
                {
                    LastError = ex.Message;
                    SetState(SessionState.AuthFailed);
                    return;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
                if (_stopping) break;
                if (reachedPlaying) backoff = 1;
                SetState(SessionState.Reconnecting);
                _stopSignal.Wait(TimeSpan.FromSeconds(backoff));
                backoff = Math.Min(backoff * 2, MaxBackoffSeconds);
            }
        }
        finally
        {
            OnSessionEnding();
            lock (_snapshotLock)
            {
                var last = _lastFrame;
                ffmpeg.av_frame_free(&last);
                _lastFrame = null;
            }
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
            ffmpeg.av_buffer_unref(&hwDevice);
            if (_state != SessionState.AuthFailed) SetState(SessionState.Stopped);
        }
    }

    void PlayOnce(AVBufferRef* hwDevice, ref bool reachedPlaying)
    {
        var fmt = ffmpeg.avformat_alloc_context();
        AVCodecContext* dec = null;
        var pkt = ffmpeg.av_packet_alloc();
        var frame = ffmpeg.av_frame_alloc();
        var sw = ffmpeg.av_frame_alloc();
        try
        {
            fmt->interrupt_callback.callback = _interrupt;
            ArmDeadline(OpenTimeoutMs);

            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "rtsp_transport", _useUdp ? "udp" : "tcp", 0);
            ffmpeg.av_dict_set(&options, "fflags", "nobuffer", 0);
            ffmpeg.av_dict_set(&options, "probesize", "32768", 0);
            ffmpeg.av_dict_set(&options, "max_delay", "0", 0);
            ffmpeg.av_dict_set(&options, "reorder_queue_size", "0", 0);
            ffmpeg.av_dict_set(&options, "timeout", "5000000", 0);
            var err = ffmpeg.avformat_open_input(&fmt, _url, null, &options);
            ffmpeg.av_dict_free(&options);
            FFmpegException.ThrowIfError(err, "open"); // on failure FFmpeg already freed fmt and set it to null

            // Live view skips avformat_find_stream_info: it waits for frames and adds seconds of latency.
            // Recording needs width/height for the MKV header, and latency does not matter there.
            if (!_decode) FFmpegException.ThrowIfError(ffmpeg.avformat_find_stream_info(fmt, null), "stream info");

            AVCodec* codec = null;
            var videoIndex = FFmpegException.ThrowIfError(
                ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0), "find video");
            var stream = fmt->streams[videoIndex];
            if (_decode) dec = OpenDecoder(codec, stream->codecpar, hwDevice);

            while (!_stopping)
            {
                ArmDeadline(StallTimeoutMs);
                FFmpegException.ThrowIfError(ffmpeg.av_read_frame(fmt, pkt), "read");
                if (pkt->stream_index == videoIndex)
                {
                    OnVideoPacket(stream, pkt);
                    if (dec is null) MarkPlaying(ref reachedPlaying);
                    else DecodePacket(dec, pkt, frame, sw, ref reachedPlaying);
                }
                ffmpeg.av_packet_unref(pkt);
            }
        }
        finally
        {
            OnConnectionClosed();
            ffmpeg.av_frame_free(&sw);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&pkt);
            ffmpeg.avcodec_free_context(&dec);
            ffmpeg.avformat_close_input(&fmt);
        }
    }

    AVCodecContext* OpenDecoder(AVCodec* codec, AVCodecParameters* parameters, AVBufferRef* hwDevice)
    {
        var dec = ffmpeg.avcodec_alloc_context3(codec);
        var err = ffmpeg.avcodec_parameters_to_context(dec, parameters);
        if (err >= 0)
        {
            dec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            dec->thread_type = ffmpeg.FF_THREAD_SLICE; // frame threading adds a frame of delay per thread
            dec->thread_count = 2;
            if (hwDevice is not null && !_forceSoftware)
            {
                dec->hw_device_ctx = ffmpeg.av_buffer_ref(hwDevice);
                dec->get_format = _getFormat;
            }
            err = ffmpeg.avcodec_open2(dec, codec, null);
        }
        if (err < 0)
        {
            ffmpeg.avcodec_free_context(&dec);
            FFmpegException.ThrowIfError(err, "open decoder");
        }
        return dec;
    }

    static AVPixelFormat GetFormat(AVCodecContext* context, AVPixelFormat* formats)
    {
        for (var p = formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
            if (*p == AVPixelFormat.AV_PIX_FMT_D3D11) return *p;
        return ffmpeg.avcodec_default_get_format(context, formats);
    }

    void DecodePacket(AVCodecContext* dec, AVPacket* pkt, AVFrame* frame, AVFrame* sw, ref bool reachedPlaying)
    {
        var again = ffmpeg.AVERROR(ffmpeg.EAGAIN);
        var err = ffmpeg.avcodec_send_packet(dec, pkt);
        if (err < 0 && err != again)
        {
            CountDecodeError(err);
            return;
        }
        while (true)
        {
            err = ffmpeg.avcodec_receive_frame(dec, frame);
            if (err == again || err == ffmpeg.AVERROR_EOF) return;
            if (err < 0)
            {
                CountDecodeError(err);
                return;
            }
            _decodeErrors = 0;
            PresentFrame(frame, sw);
            ffmpeg.av_frame_unref(frame);
            MarkPlaying(ref reachedPlaying);
        }
    }

    void CountDecodeError(int err)
    {
        if (++_decodeErrors < MaxConsecutiveDecodeErrors) return;
        _decodeErrors = 0;
        _forceSoftware = true; // persistent failures: reconnect and decode on the CPU
        throw new FFmpegException(err, "decode");
    }

    void PresentFrame(AVFrame* frame, AVFrame* sw)
    {
        var src = frame;
        if (frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11)
        {
            ffmpeg.av_frame_unref(sw);
            var err = ffmpeg.av_hwframe_transfer_data(sw, frame, 0);
            if (err < 0)
            {
                _forceSoftware = true;
                FFmpegException.ThrowIfError(err, "gpu transfer");
            }
            src = sw;
        }
        KeepForSnapshot(src);

        var (width, height) = FrameGeometry.FitSize(src->width, src->height, _targetWidth, _targetHeight);
        var target = Mailbox.Rent(width, height);
        _sws = ffmpeg.sws_getCachedContext(_sws, src->width, src->height, (AVPixelFormat)src->format,
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_sws is null) throw new InvalidOperationException("sws_getCachedContext failed");

        fixed (byte* dst = target.Data)
        {
            ffmpeg.sws_scale(_sws, src->data.ToArray(), src->linesize.ToArray(), 0, src->height,
                new byte*[] { dst, null, null, null }, new[] { target.Stride, 0, 0, 0 });
        }
        Mailbox.Publish(target);
    }

    void KeepForSnapshot(AVFrame* src)
    {
        lock (_snapshotLock)
        {
            if (_lastFrame is null) _lastFrame = ffmpeg.av_frame_alloc();
            else ffmpeg.av_frame_unref(_lastFrame);
            ffmpeg.av_frame_ref(_lastFrame, src);
        }
    }

    void MarkPlaying(ref bool reachedPlaying)
    {
        reachedPlaying = true;
        SetState(SessionState.Playing);
    }

    void ArmDeadline(int milliseconds) =>
        Interlocked.Exchange(ref _deadline, Environment.TickCount64 + milliseconds);

    void SetState(SessionState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    // Recording hooks; Task 9 replaces these with real implementations.
    partial void OnVideoPacketCore(AVStream* stream, AVPacket* pkt);
    partial void OnConnectionClosedCore();
    partial void OnSessionEndingCore();
    void OnVideoPacket(AVStream* stream, AVPacket* pkt) => OnVideoPacketCore(stream, pkt);
    void OnConnectionClosed() => OnConnectionClosedCore();
    void OnSessionEnding() => OnSessionEndingCore();
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Media.Tests --filter FullyQualifiedName~StreamSessionTests --logger "console;verbosity=detailed"`
Expected: PASS (7 tests), with a log line `First frame after N ms`.
If `Wrong_password_ends_in_AuthFailed` fails, the message shows the error code FFmpeg returned for RTSP 401. Add that code to `FFmpegException.IsAuthError` and re-run. Do not loosen the check to "any error".

- [ ] **Step 7: Commit**

```bash
git add tools/get-mediamtx.ps1 src/CamaraWin.Media tests/CamaraWin.Media.Tests
git commit -m "feat(media): low-latency RTSP session with GPU decode, scaling and reconnect

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: MKV recorder and session recording

**Files:**
- Create: `src/CamaraWin.Media/Recorder.cs`, `src/CamaraWin.Media/StreamSession.Recording.cs`
- Test: `tests/CamaraWin.Media.Tests/RecordingTests.cs`

**Interfaces:**
- Consumes: `StreamSession` partial hooks `OnVideoPacketCore`, `OnConnectionClosedCore`, `OnSessionEndingCore` (Task 8).
- Produces: `sealed unsafe class Recorder(string path, AVCodecParameters* input, AVRational inputTimeBase)` with `string Path`, `string? Error`, `Task Completion`, `bool Enqueue(AVPacket*)`, `void Complete()`. On `StreamSession`: `bool IsRecording`, `event Action<string>? RecordingFailed`, `void StartRecording(Func<string> pathFactory)`, `Task StopRecordingAsync()`. Recording is meant for `decode: false` sessions on the mainstream.

- [ ] **Step 1: Write the failing tests** — `tests/CamaraWin.Media.Tests/RecordingTests.cs`

```csharp
using System.Diagnostics;
using CamaraWin.Media.Tests.Rtsp;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class RecordingTests(RtspTestServer server) : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-rec-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static string Probe(string args)
    {
        var ffprobe = TestUtil.FindOnPath("ffprobe.exe") ?? throw new InvalidOperationException("ffprobe not on PATH");
        using var p = Process.Start(new ProcessStartInfo(ffprobe, args)
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
        })!;
        var text = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return text;
    }

    StreamSession OpenHeadless()
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(server.Url("open"), decode: false);
    }

    [SkippableFact]
    public async Task Records_a_valid_mkv_that_starts_on_a_keyframe()
    {
        var path = Path.Combine(_dir, "cam", "clip.mkv");
        using var session = OpenHeadless();
        session.StartRecording(() => path);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Playing, TimeSpan.FromSeconds(10)), session.LastError);
        Thread.Sleep(3000);

        await session.StopRecordingAsync();
        session.Stop();

        Assert.False(session.IsRecording);
        Assert.Equal("h264,640,360", Probe($"-v error -select_streams v:0 -show_entries stream=codec_name,width,height -of csv=p=0 \"{path}\""));
        var duration = double.Parse(Probe($"-v error -show_entries format=duration -of csv=p=0 \"{path}\""),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(duration, 1.5, 6);
        Assert.Equal("1", Probe($"-v error -select_streams v:0 -read_intervals %+#1 -show_entries frame=key_frame -of csv=p=0 \"{path}\""));
    }

    [SkippableFact]
    public void Unwritable_path_raises_RecordingFailed()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "file where a folder should be");

        using var session = OpenHeadless();
        string? failure = null;
        session.RecordingFailed += message => failure = message;
        session.StartRecording(() => Path.Combine(blocker, "sub", "clip.mkv"));
        session.Start();

        Assert.True(TestUtil.WaitFor(() => failure is not null, TimeSpan.FromSeconds(10)));
        Assert.False(session.IsRecording);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Media.Tests --filter FullyQualifiedName~RecordingTests`
Expected: build FAILS (`StartRecording` not found).

- [ ] **Step 3: Implement**

`src/CamaraWin.Media/Recorder.cs`:

```csharp
using System.Collections.Concurrent;
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

/// <summary>
/// Copies compressed packets into a Matroska file without re-encoding. Disk I/O runs on its own
/// thread behind a bounded queue so a slow disk can never stall the network reader.
/// </summary>
public sealed unsafe class Recorder
{
    const int QueueCapacity = 512;

    readonly BlockingCollection<nint> _queue = new(QueueCapacity);
    readonly AVFormatContext* _output;
    readonly AVRational _inputTimeBase;
    readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    long _startTimestamp = ffmpeg.AV_NOPTS_VALUE;

    public Recorder(string path, AVCodecParameters* input, AVRational inputTimeBase)
    {
        Path = path;
        _inputTimeBase = inputTimeBase;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        AVFormatContext* output = null;
        FFmpegException.ThrowIfError(ffmpeg.avformat_alloc_output_context2(&output, null, "matroska", path), "create mkv");
        try
        {
            var stream = ffmpeg.avformat_new_stream(output, null);
            FFmpegException.ThrowIfError(ffmpeg.avcodec_parameters_copy(stream->codecpar, input), "copy codec parameters");
            stream->codecpar->codec_tag = 0;
            stream->time_base = inputTimeBase;
            FFmpegException.ThrowIfError(ffmpeg.avio_open(&output->pb, path, ffmpeg.AVIO_FLAG_WRITE), "open file");
            FFmpegException.ThrowIfError(ffmpeg.avformat_write_header(output, null), "write header");
        }
        catch
        {
            if (output->pb is not null) ffmpeg.avio_closep(&output->pb);
            ffmpeg.avformat_free_context(output);
            throw;
        }
        _output = output;
        new Thread(WriteLoop) { IsBackground = true, Name = "Recorder" }.Start();
    }

    public string Path { get; }
    public string? Error { get; private set; }
    public Task Completion => _done.Task;

    /// <summary>Queues a copy of the packet. False means recording must stop (see <see cref="Error"/>).</summary>
    public bool Enqueue(AVPacket* packet)
    {
        if (Error is not null || _queue.IsAddingCompleted) return false;
        var clone = ffmpeg.av_packet_clone(packet);
        if (clone is null) return false;
        if (_queue.TryAdd((nint)clone)) return true;
        ffmpeg.av_packet_free(&clone);
        Error = "El disco no da abasto; grabación detenida.";
        return false;
    }

    /// <summary>Stops accepting packets; the writer drains the queue and finalizes the file.</summary>
    public void Complete()
    {
        if (!_queue.IsAddingCompleted) _queue.CompleteAdding();
    }

    void WriteLoop()
    {
        try
        {
            foreach (var handle in _queue.GetConsumingEnumerable())
            {
                var packet = (AVPacket*)handle;
                try
                {
                    if (Error is null) Write(packet);
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                }
            }
        }
        finally
        {
            ffmpeg.av_write_trailer(_output);
            var output = _output;
            ffmpeg.avio_closep(&output->pb);
            ffmpeg.avformat_free_context(output);
            _done.TrySetResult();
        }
    }

    void Write(AVPacket* packet)
    {
        var noTimestamp = ffmpeg.AV_NOPTS_VALUE;
        if (_startTimestamp == noTimestamp)
            _startTimestamp = packet->dts != noTimestamp ? packet->dts : packet->pts;
        if (packet->pts != noTimestamp) packet->pts -= _startTimestamp;
        if (packet->dts != noTimestamp) packet->dts -= _startTimestamp;
        packet->stream_index = 0;
        packet->pos = -1;
        ffmpeg.av_packet_rescale_ts(packet, _inputTimeBase, _output->streams[0]->time_base);

        var err = ffmpeg.av_interleaved_write_frame(_output, packet);
        if (err < 0) Error = $"Error al escribir: {FFmpegException.Describe(err)}";
    }
}
```

`src/CamaraWin.Media/StreamSession.Recording.cs`:

```csharp
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public sealed unsafe partial class StreamSession
{
    readonly object _recordLock = new();
    Func<string>? _recordPathFactory; // non-null while recording is requested
    Recorder? _recorder;

    public bool IsRecording
    {
        get { lock (_recordLock) return _recordPathFactory is not null; }
    }

    public event Action<string>? RecordingFailed;

    /// <summary>Records from the next keyframe. After a reconnect a new file is started.</summary>
    public void StartRecording(Func<string> pathFactory)
    {
        lock (_recordLock) _recordPathFactory = pathFactory;
    }

    public Task StopRecordingAsync()
    {
        Recorder? recorder;
        lock (_recordLock)
        {
            _recordPathFactory = null;
            recorder = _recorder;
            _recorder = null;
        }
        recorder?.Complete();
        return recorder?.Completion ?? Task.CompletedTask;
    }

    partial void OnVideoPacketCore(AVStream* stream, AVPacket* pkt)
    {
        string? failure = null;
        lock (_recordLock)
        {
            if (_recordPathFactory is null) return;
            if (_recorder is null)
            {
                if ((pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0) return;
                try
                {
                    _recorder = new Recorder(_recordPathFactory(), stream->codecpar, stream->time_base);
                }
                catch (Exception ex)
                {
                    _recordPathFactory = null;
                    failure = ex.Message;
                }
            }
            if (_recorder is not null && !_recorder.Enqueue(pkt))
            {
                failure = _recorder.Error ?? "Grabación detenida.";
                _recorder.Complete();
                _recorder = null;
                _recordPathFactory = null;
            }
        }
        if (failure is not null) RecordingFailed?.Invoke(failure);
    }

    partial void OnConnectionClosedCore()
    {
        Recorder? recorder;
        lock (_recordLock)
        {
            recorder = _recorder;
            _recorder = null;
        }
        recorder?.Complete();
    }

    partial void OnSessionEndingCore() => OnConnectionClosedCore();
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Media.Tests`
Expected: PASS (all Media tests).

- [ ] **Step 5: Commit**

```bash
git add src/CamaraWin.Media tests/CamaraWin.Media.Tests
git commit -m "feat(media): MKV packet recorder driven by headless sessions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: PNG snapshots

**Files:**
- Create: `src/CamaraWin.Media/SnapshotWriter.cs`, `src/CamaraWin.Media/StreamSession.Snapshot.cs`
- Test: `tests/CamaraWin.Media.Tests/SnapshotTests.cs`

**Interfaces:**
- Consumes: `StreamSession._lastFrame`, `_snapshotLock` (Task 8).
- Produces: `static unsafe void SnapshotWriter.SavePng(AVFrame* frame, string path)`; `Task<bool> StreamSession.SaveSnapshotAsync(string path)`, which returns false when no frame has been decoded yet.

- [ ] **Step 1: Write the failing tests** — `tests/CamaraWin.Media.Tests/SnapshotTests.cs`

```csharp
using System.Buffers.Binary;
using CamaraWin.Media.Tests.Rtsp;

namespace CamaraWin.Media.Tests;

[Collection("rtsp")]
public sealed class SnapshotTests(RtspTestServer server) : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "camarawin-snap-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [SkippableFact]
    public async Task Saves_png_at_native_resolution_even_when_scaled()
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        using var session = new StreamSession(server.Url("open"));
        session.SetTargetSize(160, 90);
        session.Start();
        Assert.True(TestUtil.WaitFor(() => session.Mailbox.Sequence > 0, TimeSpan.FromSeconds(10)), session.LastError);

        var path = Path.Combine(_dir, "snap.png");
        Assert.True(await session.SaveSnapshotAsync(path));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        Assert.Equal(640, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(360, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    [Fact]
    public async Task Returns_false_before_first_frame()
    {
        FFmpegLoader.Initialize();
        using var session = new StreamSession("rtsp://127.0.0.1:1/none");
        Assert.False(await session.SaveSnapshotAsync(Path.Combine(_dir, "none.png")));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/CamaraWin.Media.Tests --filter FullyQualifiedName~SnapshotTests`
Expected: build FAILS (`SaveSnapshotAsync` not found).

- [ ] **Step 3: Implement**

`src/CamaraWin.Media/SnapshotWriter.cs`:

```csharp
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public static unsafe class SnapshotWriter
{
    public static void SavePng(AVFrame* source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_PNG);
        if (codec is null) throw new InvalidOperationException("FFmpeg sin codificador PNG");

        var encoder = ffmpeg.avcodec_alloc_context3(codec);
        var rgb = ffmpeg.av_frame_alloc();
        var packet = ffmpeg.av_packet_alloc();
        SwsContext* sws = null;
        try
        {
            encoder->width = source->width;
            encoder->height = source->height;
            encoder->pix_fmt = AVPixelFormat.AV_PIX_FMT_RGB24;
            encoder->time_base = new AVRational { num = 1, den = 1 };
            FFmpegException.ThrowIfError(ffmpeg.avcodec_open2(encoder, codec, null), "open png encoder");

            rgb->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
            rgb->width = source->width;
            rgb->height = source->height;
            FFmpegException.ThrowIfError(ffmpeg.av_frame_get_buffer(rgb, 0), "alloc rgb frame");

            sws = ffmpeg.sws_getContext(source->width, source->height, (AVPixelFormat)source->format,
                source->width, source->height, AVPixelFormat.AV_PIX_FMT_RGB24, (int)SwsFlags.SWS_BICUBIC, null, null, null);
            if (sws is null) throw new InvalidOperationException("sws_getContext failed");
            ffmpeg.sws_scale(sws, source->data.ToArray(), source->linesize.ToArray(), 0, source->height,
                rgb->data.ToArray(), rgb->linesize.ToArray());

            FFmpegException.ThrowIfError(ffmpeg.avcodec_send_frame(encoder, rgb), "encode png");
            FFmpegException.ThrowIfError(ffmpeg.avcodec_receive_packet(encoder, packet), "encode png");
            using var file = File.Create(path);
            file.Write(new ReadOnlySpan<byte>(packet->data, packet->size));
        }
        finally
        {
            ffmpeg.sws_freeContext(sws);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&rgb);
            ffmpeg.avcodec_free_context(&encoder);
        }
    }
}
```

`src/CamaraWin.Media/StreamSession.Snapshot.cs`:

```csharp
using FFmpeg.AutoGen;

namespace CamaraWin.Media;

public sealed unsafe partial class StreamSession
{
    /// <summary>Saves the last decoded frame at its native resolution. False if nothing was decoded yet.</summary>
    public Task<bool> SaveSnapshotAsync(string path) => Task.Run(() =>
    {
        AVFrame* copy;
        lock (_snapshotLock)
        {
            if (_lastFrame is null) return false;
            copy = ffmpeg.av_frame_clone(_lastFrame);
        }
        try
        {
            SnapshotWriter.SavePng(copy, path);
            return true;
        }
        finally
        {
            ffmpeg.av_frame_free(&copy);
        }
    });
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/CamaraWin.Media.Tests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/CamaraWin.Media tests/CamaraWin.Media.Tests
git commit -m "feat(media): native-resolution PNG snapshots

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: WPF shell — main window grid and live camera tiles

**Files:**
- Create: `src/CamaraWin.App/CamaraWin.App.csproj` (template), `src/CamaraWin.App/App.xaml`, `src/CamaraWin.App/App.xaml.cs`, `src/CamaraWin.App/MainWindow.xaml`, `src/CamaraWin.App/MainWindow.xaml.cs`, `src/CamaraWin.App/CameraTile.xaml`, `src/CamaraWin.App/CameraTile.xaml.cs`

**Interfaces:**
- Consumes: `Camera`, `StreamKind`, `StreamUrlBuilder`, `CameraStore`, `SettingsStore`, `AppSettings`, `GridLayout` (Core); `FFmpegLoader`, `StreamSession`, `SessionState` (Media).
- Produces: `sealed partial class CameraTile : UserControl, IDisposable` with ctor `CameraTile(Camera camera, StreamKind kind)`, `Camera Camera`, `StreamKind Kind`, `void RequestStop()`, `void Dispose()`. `MainWindow` fields `_store`, `_settingsStore`, `_settings`, `_cameras`, `_tiles`, and methods `RebuildGrid()`, `CreateTile(Camera)`, `DisposeTile(Guid)`, `SaveCameras()`.

- [ ] **Step 1: Create the project**

```bash
dotnet new wpf -n CamaraWin.App -o src/CamaraWin.App
sed -i '/<TargetFramework>/d' src/CamaraWin.App/CamaraWin.App.csproj
dotnet sln CamaraWin.slnx add src/CamaraWin.App
dotnet add src/CamaraWin.App reference src/CamaraWin.Core src/CamaraWin.Media
```

Add to the first `<PropertyGroup>` of `src/CamaraWin.App/CamaraWin.App.csproj`:

```xml
    <AssemblyName>CamaraWin</AssemblyName>
    <CopyFFmpeg>true</CopyFFmpeg>
```

- [ ] **Step 2: App** — replace `src/CamaraWin.App/App.xaml`

```xml
<Application x:Class="CamaraWin.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <Style x:Key="TileButton" TargetType="Button">
            <Setter Property="Width" Value="30"/>
            <Setter Property="Height" Value="30"/>
            <Setter Property="Margin" Value="2,0"/>
            <Setter Property="Background" Value="#B0202020"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="FontFamily" Value="Segoe UI Emoji"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>
    </Application.Resources>
</Application>
```

Replace `src/CamaraWin.App/App.xaml.cs`:

```csharp
using System.Windows;
using CamaraWin.Media;

namespace CamaraWin.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            FFmpegLoader.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CamaraWin", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        new MainWindow().Show();
    }
}
```

- [ ] **Step 3: Camera tile** — `src/CamaraWin.App/CameraTile.xaml`

```xml
<UserControl x:Class="CamaraWin.App.CameraTile"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Black" AllowDrop="True">
    <Border x:Name="Frame" BorderBrush="#2A2A2A" BorderThickness="1">
        <Grid>
            <Image x:Name="Video" Stretch="Uniform" RenderOptions.BitmapScalingMode="Linear"/>
            <TextBlock x:Name="StatusLabel" Foreground="#DDD" FontSize="14" TextAlignment="Center"
                       TextWrapping="Wrap" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            <Border Background="#80000000" Padding="6,2" CornerRadius="0,0,4,0"
                    HorizontalAlignment="Left" VerticalAlignment="Top">
                <StackPanel Orientation="Horizontal">
                    <Ellipse x:Name="RecDot" Width="10" Height="10" Fill="Red" Margin="0,0,6,0" Visibility="Collapsed"/>
                    <TextBlock x:Name="NameLabel" Foreground="White"/>
                </StackPanel>
            </Border>
            <StackPanel x:Name="Actions" Orientation="Horizontal" Margin="4"
                        HorizontalAlignment="Right" VerticalAlignment="Top" Visibility="Collapsed"/>
        </Grid>
    </Border>
</UserControl>
```

`src/CamaraWin.App/CameraTile.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public sealed partial class CameraTile : UserControl, IDisposable
{
    readonly StreamSession _session;
    WriteableBitmap? _bitmap;
    long _frameSequence;
    bool _disposed;

    public CameraTile(Camera camera, StreamKind kind)
    {
        InitializeComponent();
        Camera = camera;
        Kind = kind;
        NameLabel.Text = camera.Name;

        _session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
        _session.StateChanged += state => Dispatcher.BeginInvoke(() => ShowState(state));
        SizeChanged += (_, _) => UpdateTargetSize();
        CompositionTarget.Rendering += OnRendering;
        ShowState(SessionState.Connecting);
        _session.Start();
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }

    void OnRendering(object? sender, EventArgs e) =>
        _session.Mailbox.TryRead(ref _frameSequence, frame =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                Video.Source = _bitmap;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Data, frame.Stride, 0);
        });

    void UpdateTargetSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _session.SetTargetSize((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
    }

    void ShowState(SessionState state)
    {
        if (_disposed) return;
        StatusLabel.Text = state switch
        {
            SessionState.Connecting => "Conectando…",
            SessionState.Reconnecting => "Reconectando…",
            SessionState.AuthFailed => "Credenciales incorrectas",
            SessionState.Stopped => "Detenida",
            _ => "",
        };
        Video.Opacity = state == SessionState.Playing ? 1 : 0.4;
    }

    public void RequestStop() => _session.RequestStop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        _session.Dispose();
    }
}
```

- [ ] **Step 4: Main window** — replace `src/CamaraWin.App/MainWindow.xaml`

```xml
<Window x:Class="CamaraWin.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="CamaraWin" Width="1280" Height="800" Background="#111">
    <DockPanel>
        <ToolBarTray x:Name="TopBar" DockPanel.Dock="Top">
            <ToolBar x:Name="MainToolBar"/>
        </ToolBarTray>
        <StatusBar x:Name="BottomBar" DockPanel.Dock="Bottom">
            <TextBlock x:Name="StatusText"/>
        </StatusBar>
        <Grid>
            <UniformGrid x:Name="TileGrid"/>
            <StackPanel x:Name="EmptyState" HorizontalAlignment="Center" VerticalAlignment="Center">
                <TextBlock Text="No hay cámaras" Foreground="#AAA" FontSize="20" HorizontalAlignment="Center"/>
            </StackPanel>
        </Grid>
    </DockPanel>
</Window>
```

Replace `src/CamaraWin.App/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class MainWindow : Window
{
    readonly CameraStore _store = new(CameraStore.DefaultPath);
    readonly SettingsStore _settingsStore = new(SettingsStore.DefaultPath);
    readonly AppSettings _settings;
    readonly List<Camera> _cameras;
    readonly Dictionary<Guid, CameraTile> _tiles = [];

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        _cameras = [.. _store.Load()];
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;
        RebuildGrid();
    }

    void RebuildGrid()
    {
        var ordered = _cameras.OrderBy(c => c.Order).ToList();
        var visible = ordered.Take(GridLayout.VisibleCount(ordered.Count, _settings.GridMode)).ToList();
        var size = GridLayout.Compute(visible.Count, _settings.GridMode);

        foreach (var id in _tiles.Keys.Except(visible.Select(c => c.Id)).ToList()) DisposeTile(id);

        TileGrid.Children.Clear();
        TileGrid.Rows = size.Rows;
        TileGrid.Columns = size.Columns;
        foreach (var camera in visible)
        {
            if (!_tiles.TryGetValue(camera.Id, out var tile))
            {
                tile = CreateTile(camera);
                _tiles[camera.Id] = tile;
            }
            TileGrid.Children.Add(tile);
        }
        EmptyState.Visibility = _cameras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    CameraTile CreateTile(Camera camera) => new(camera, StreamKind.Sub);

    void DisposeTile(Guid id)
    {
        if (!_tiles.Remove(id, out var tile)) return;
        TileGrid.Children.Remove(tile);
        tile.Dispose();
    }

    void SaveCameras() => _store.Save(_cameras);

    protected override void OnClosed(EventArgs e)
    {
        foreach (var tile in _tiles.Values) tile.RequestStop();
        foreach (var tile in _tiles.Values) tile.Dispose();
        base.OnClosed(e);
    }
}
```

In `App.xaml` there is no `StartupUri` (the window is created in `OnStartup`).

- [ ] **Step 5: Build**

Run: `dotnet build CamaraWin.slnx`
Expected: `Build succeeded`, 0 errors. `src/CamaraWin.App/bin/Debug/net10.0-windows/ffmpeg/avcodec-63.dll` exists.

- [ ] **Step 6: Manual check against a local stream**

Start a test source (two terminals, or run both in the background):

```bash
tools/bin/mediamtx.exe tools/bin/mediamtx.yml
```

```bash
ffmpeg -re -f lavfi -i testsrc2=size=1280x720:rate=25 -c:v libx264 -preset ultrafast -tune zerolatency -g 25 -f rtsp rtsp://127.0.0.1:8554/cam1
```

Seed a camera with a custom URL. This overwrites `%AppData%\CamaraWin\cameras.json`, so check first that the file does not exist or has nothing to keep:

```bash
mkdir -p "$APPDATA/CamaraWin" && printf '[{"id":"11111111-1111-1111-1111-111111111111","name":"Prueba","brand":"Custom","host":"","port":554,"user":"","passwordProtected":"","mainUrlOverride":"rtsp://127.0.0.1:8554/cam1","subUrlOverride":null,"useUdp":false,"order":0}]' > "$APPDATA/CamaraWin/cameras.json"
dotnet run --project src/CamaraWin.App
```

Expected: one tile named "Prueba" shows the moving test pattern within about 1 s. The on-screen clock in `testsrc2` lags by less than ~300 ms (compare against the ffmpeg console `time=`). Resizing the window keeps the image sharp. Closing the window exits in under 1 s. Then delete the seeded file:

```bash
rm "$APPDATA/CamaraWin/cameras.json"
```

- [ ] **Step 7: Commit**

```bash
git add CamaraWin.slnx src/CamaraWin.App
git commit -m "feat(app): WPF shell with live camera grid

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Add/edit dialog, delete, snapshot and record actions

**Files:**
- Create: `src/CamaraWin.App/AddCameraDialog.xaml`, `src/CamaraWin.App/AddCameraDialog.xaml.cs`
- Modify: `src/CamaraWin.App/CameraTile.xaml` (action buttons), `src/CamaraWin.App/CameraTile.xaml.cs` (ctor signature, actions), `src/CamaraWin.App/MainWindow.xaml` (toolbar, empty-state button, status link), `src/CamaraWin.App/MainWindow.xaml.cs` (handlers)

**Interfaces:**
- Consumes: everything from Task 11; `AppPaths`, `StreamSession.SaveSnapshotAsync`, `StartRecording`, `StopRecordingAsync`, `RecordingFailed`.
- Produces: `AddCameraDialog(Camera? initial = null, bool isNew = true)` with `Camera Result`. `CameraTile(Camera camera, StreamKind kind, bool manage = true)` with events `Action<CameraTile>? EditRequested`, `Action<CameraTile>? DeleteRequested`, `Action<string, string?>? Notify` (message, path to reveal). `MainWindow.Notify(string message, string? revealPath)` is `internal`.

- [ ] **Step 1: Dialog** — `src/CamaraWin.App/AddCameraDialog.xaml`

```xml
<Window x:Class="CamaraWin.App.AddCameraDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Añadir cámara" Width="480" SizeToContent="Height"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <StackPanel Margin="16">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="110"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/>
            </Grid.RowDefinitions>
            <Grid.Resources>
                <Style TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="Margin" Value="0,4"/></Style>
                <Style TargetType="TextBox"><Setter Property="Margin" Value="0,4"/></Style>
                <Style TargetType="PasswordBox"><Setter Property="Margin" Value="0,4"/></Style>
                <Style TargetType="ComboBox"><Setter Property="Margin" Value="0,4"/></Style>
            </Grid.Resources>
            <TextBlock Text="Marca"/>
            <ComboBox x:Name="BrandBox" Grid.Column="1" SelectionChanged="Brand_Changed">
                <ComboBoxItem Content="Tapo"/>
                <ComboBoxItem Content="Imou"/>
                <ComboBoxItem Content="Otra (RTSP)"/>
            </ComboBox>
            <TextBlock Grid.Row="1" Text="Nombre"/>
            <TextBox x:Name="NameBox" Grid.Row="1" Grid.Column="1"/>
            <TextBlock Grid.Row="2" Text="IP"/>
            <TextBox x:Name="HostBox" Grid.Row="2" Grid.Column="1"/>
            <TextBlock Grid.Row="3" Text="Puerto RTSP"/>
            <TextBox x:Name="PortBox" Grid.Row="3" Grid.Column="1"/>
            <TextBlock Grid.Row="4" Text="Usuario"/>
            <TextBox x:Name="UserBox" Grid.Row="4" Grid.Column="1"/>
            <TextBlock Grid.Row="5" Text="Contraseña"/>
            <PasswordBox x:Name="PasswordInput" Grid.Row="5" Grid.Column="1"/>
        </Grid>
        <Expander x:Name="AdvancedBox" Header="Avanzado" Margin="0,8,0,0">
            <StackPanel Margin="0,4,0,0">
                <TextBlock Text="URL principal (opcional salvo en «Otra»)"/>
                <TextBox x:Name="MainUrlBox" Margin="0,2,0,6"/>
                <TextBlock Text="URL secundaria (opcional)"/>
                <TextBox x:Name="SubUrlBox" Margin="0,2,0,6"/>
                <CheckBox x:Name="UdpBox" Content="Usar UDP (menos retardo, puede mostrar artefactos)"/>
            </StackPanel>
        </Expander>
        <TextBlock x:Name="HintText" Foreground="Gray" TextWrapping="Wrap" Margin="0,8"/>
        <Border Height="180" Background="Black" Margin="0,4">
            <Grid>
                <Image x:Name="Preview" Stretch="Uniform"/>
                <TextBlock x:Name="TestStatus" Foreground="White" TextWrapping="Wrap"
                           HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center"/>
            </Grid>
        </Border>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="Probar" Width="90" Margin="0,0,8,0" Click="Test_Click"/>
            <Button Content="Guardar" Width="90" Margin="0,0,8,0" IsDefault="True" Click="Save_Click"/>
            <Button Content="Cancelar" Width="90" IsCancel="True"/>
        </StackPanel>
    </StackPanel>
</Window>
```

`src/CamaraWin.App/AddCameraDialog.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CamaraWin.Core;
using CamaraWin.Media;

namespace CamaraWin.App;

public partial class AddCameraDialog : Window
{
    readonly Camera _camera;
    StreamSession? _test;
    WriteableBitmap? _previewBitmap;
    long _previewSequence;

    public AddCameraDialog(Camera? initial = null, bool isNew = true)
    {
        InitializeComponent();
        _camera = initial?.Clone() ?? new Camera { Brand = Brand.Tapo };
        Title = isNew ? "Añadir cámara" : "Editar cámara";

        NameBox.Text = _camera.Name;
        HostBox.Text = _camera.Host;
        PortBox.Text = _camera.Port.ToString();
        UserBox.Text = _camera.User;
        PasswordInput.Password = _camera.Password;
        MainUrlBox.Text = _camera.MainUrlOverride ?? "";
        SubUrlBox.Text = _camera.SubUrlOverride ?? "";
        UdpBox.IsChecked = _camera.UseUdp;
        BrandBox.SelectedIndex = (int)_camera.Brand;

        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            StopTest();
        };
    }

    public Camera Result => _camera;

    void Brand_Changed(object sender, SelectionChangedEventArgs e)
    {
        var brand = (Brand)BrandBox.SelectedIndex;
        if (brand == Brand.Imou && UserBox.Text.Length == 0) UserBox.Text = "admin";
        if (brand == Brand.Custom) AdvancedBox.IsExpanded = true;
        HintText.Text = brand switch
        {
            Brand.Tapo => "Usa el usuario y la contraseña de la «Cuenta de cámara» (app Tapo › Ajustes avanzados).",
            Brand.Imou => "Usuario «admin». La contraseña es el código de seguridad de la pegatina. Si no conecta, activa RTSP/ONVIF en la app Imou.",
            _ => "Pon la URL RTSP completa en «Avanzado». El usuario y la contraseña se añaden si la URL no los incluye.",
        };
    }

    bool TryReadForm(out string error)
    {
        var brand = (Brand)BrandBox.SelectedIndex;
        var mainUrl = MainUrlBox.Text.Trim();
        error = "";
        if (NameBox.Text.Trim().Length == 0) error = "Pon un nombre a la cámara.";
        else if (brand != Brand.Custom && HostBox.Text.Trim().Length == 0) error = "La IP es obligatoria.";
        else if (brand == Brand.Custom && mainUrl.Length == 0) error = "La URL principal es obligatoria.";
        else if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535) error = "Puerto no válido.";
        if (error.Length > 0) return false;

        _camera.Brand = brand;
        _camera.Name = NameBox.Text.Trim();
        _camera.Host = HostBox.Text.Trim();
        _camera.Port = int.Parse(PortBox.Text);
        _camera.User = UserBox.Text.Trim();
        _camera.Password = PasswordInput.Password;
        _camera.MainUrlOverride = mainUrl.Length == 0 ? null : mainUrl;
        _camera.SubUrlOverride = SubUrlBox.Text.Trim() is { Length: > 0 } sub ? sub : null;
        _camera.UseUdp = UdpBox.IsChecked == true;
        return true;
    }

    void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            TestStatus.Text = error;
            return;
        }
        StopTest();
        var session = new StreamSession(StreamUrlBuilder.Build(_camera, StreamKind.Sub), _camera.UseUdp);
        session.SetTargetSize(640, 360);
        session.StateChanged += state => Dispatcher.BeginInvoke(() =>
        {
            if (_test != session) return;
            TestStatus.Text = state switch
            {
                SessionState.Connecting => "Conectando…",
                SessionState.Reconnecting => $"No conecta: {session.LastError}",
                SessionState.AuthFailed => "Usuario o contraseña incorrectos.",
                _ => "",
            };
        });
        _test = session;
        session.Start();
    }

    void OnRendering(object? sender, EventArgs e) =>
        _test?.Mailbox.TryRead(ref _previewSequence, frame =>
        {
            if (_previewBitmap is null || _previewBitmap.PixelWidth != frame.Width || _previewBitmap.PixelHeight != frame.Height)
            {
                _previewBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                Preview.Source = _previewBitmap;
            }
            _previewBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Data, frame.Stride, 0);
        });

    void StopTest()
    {
        _test?.Dispose();
        _test = null;
        _previewSequence = 0;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var error))
        {
            HintText.Text = error;
            return;
        }
        DialogResult = true;
    }
}
```

- [ ] **Step 2: Tile actions** — in `src/CamaraWin.App/CameraTile.xaml`, replace the empty `Actions` StackPanel with:

```xml
            <StackPanel x:Name="Actions" Orientation="Horizontal" Margin="4"
                        HorizontalAlignment="Right" VerticalAlignment="Top" Visibility="Collapsed">
                <Button x:Name="SnapshotButton" Content="📷" ToolTip="Captura" Style="{StaticResource TileButton}" Click="Snapshot_Click"/>
                <Button x:Name="RecordButton" Content="⏺" ToolTip="Grabar" Style="{StaticResource TileButton}" Click="Record_Click"/>
                <Button x:Name="EditButton" Content="✎" ToolTip="Editar" Style="{StaticResource TileButton}" Click="Edit_Click"/>
                <Button x:Name="DeleteButton" Content="🗑" ToolTip="Eliminar" Style="{StaticResource TileButton}" Click="Delete_Click"/>
            </StackPanel>
```

In `src/CamaraWin.App/CameraTile.xaml.cs`, change the constructor signature and add the lines marked below:

```csharp
    public CameraTile(Camera camera, StreamKind kind, bool manage = true)
    {
        InitializeComponent();
        Camera = camera;
        Kind = kind;
        _manage = manage;
        NameLabel.Text = camera.Name;
        EditButton.Visibility = DeleteButton.Visibility = manage ? Visibility.Visible : Visibility.Collapsed;
        MouseEnter += (_, _) => Actions.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => Actions.Visibility = Visibility.Collapsed;

        _session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
        _session.StateChanged += state => Dispatcher.BeginInvoke(() => ShowState(state));
        SizeChanged += (_, _) => UpdateTargetSize();
        CompositionTarget.Rendering += OnRendering;
        ShowState(SessionState.Connecting);
        _session.Start();
    }
```

Add these members to `CameraTile`:

```csharp
    readonly bool _manage;
    StreamSession? _recording;

    public event Action<CameraTile>? EditRequested;
    public event Action<CameraTile>? DeleteRequested;
    public event Action<string, string?>? Notify;

    void Edit_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(this);

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this);

    async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        var path = AppPaths.SnapshotFile(Camera.Name, DateTime.Now);
        SnapshotButton.IsEnabled = false;
        try
        {
            Notify?.Invoke(await SaveMainStreamSnapshotAsync(path)
                ? $"Captura guardada: {path}"
                : "Aún no hay imagen para capturar.", File.Exists(path) ? path : null);
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"No se pudo guardar la captura: {ex.Message}", null);
        }
        finally
        {
            SnapshotButton.IsEnabled = true;
        }
    }

    /// <summary>Grid tiles show the substream; grab one full-resolution frame from the mainstream instead.</summary>
    async Task<bool> SaveMainStreamSnapshotAsync(string path)
    {
        if (Kind == StreamKind.Main) return await _session.SaveSnapshotAsync(path);
        using var main = new StreamSession(StreamUrlBuilder.Build(Camera, StreamKind.Main), Camera.UseUdp);
        main.SetTargetSize(2, 2);
        main.Start();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (main.Mailbox.Sequence > 0) return await main.SaveSnapshotAsync(path);
            await Task.Delay(100);
        }
        return await _session.SaveSnapshotAsync(path);
    }

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recording is not null)
        {
            StopRecording();
            Notify?.Invoke($"Grabación guardada en {AppPaths.RecordingsDirectory}", AppPaths.RecordingsDirectory);
            return;
        }
        var name = Camera.Name;
        var recording = new StreamSession(StreamUrlBuilder.Build(Camera, StreamKind.Main), Camera.UseUdp, decode: false);
        recording.StartRecording(() => AppPaths.RecordingFile(name, DateTime.Now));
        recording.RecordingFailed += message => Dispatcher.BeginInvoke(() =>
        {
            if (_recording != recording) return;
            StopRecording();
            Notify?.Invoke($"Grabación de {name} detenida: {message}", null);
        });
        recording.StateChanged += state =>
        {
            if (state != SessionState.AuthFailed) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_recording != recording) return;
                StopRecording();
                Notify?.Invoke($"No se pudo grabar {name}: credenciales incorrectas.", null);
            });
        };
        _recording = recording;
        recording.Start();
        RecDot.Visibility = Visibility.Visible;
        RecordButton.Content = "⏹";
        RecordButton.ToolTip = "Detener grabación";
        Notify?.Invoke($"Grabando {name}…", null);
    }

    void StopRecording()
    {
        var recording = _recording;
        if (recording is null) return;
        _recording = null;
        RecDot.Visibility = Visibility.Collapsed;
        RecordButton.Content = "⏺";
        RecordButton.ToolTip = "Grabar";
        _ = Task.Run(async () =>
        {
            await recording.StopRecordingAsync();
            recording.Dispose();
        });
    }
```

Replace `Dispose()` in `CameraTile` so an active recording is finalized before exit:

```csharp
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        if (_recording is { } recording)
        {
            _recording = null;
            recording.StopRecordingAsync().Wait(TimeSpan.FromSeconds(3));
            recording.Dispose();
        }
        _session.Dispose();
    }
```

- [ ] **Step 3: Main window toolbar and handlers**

In `src/CamaraWin.App/MainWindow.xaml`, replace `<ToolBar x:Name="MainToolBar"/>` with:

```xml
            <ToolBar x:Name="MainToolBar">
                <Button Content="➕ Añadir cámara" Click="AddCamera_Click"/>
                <Separator/>
                <Button Content="📁 Grabaciones" Click="OpenRecordings_Click"/>
            </ToolBar>
```

Replace the status bar `TextBlock` with:

```xml
            <TextBlock x:Name="StatusText" Cursor="Hand" MouseLeftButtonUp="StatusText_Click"/>
```

Add a button under the "No hay cámaras" text in `EmptyState`:

```xml
                <Button Content="Añade tu primera cámara" Margin="0,12,0,0" Padding="12,6" Click="AddCamera_Click"/>
```

In `src/CamaraWin.App/MainWindow.xaml.cs` add `using System.Diagnostics;`, replace `CreateTile` and add the handlers:

```csharp
    string? _statusRevealPath;

    CameraTile CreateTile(Camera camera)
    {
        var tile = new CameraTile(camera, StreamKind.Sub);
        tile.EditRequested += EditCamera;
        tile.DeleteRequested += DeleteCamera;
        tile.Notify += Notify;
        return tile;
    }

    void AddCamera_Click(object sender, RoutedEventArgs e) => AddCamera(null);

    void AddCamera(Camera? prefilled)
    {
        var dialog = new AddCameraDialog(prefilled) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var camera = dialog.Result;
        camera.Order = _cameras.Count == 0 ? 0 : _cameras.Max(c => c.Order) + 1;
        _cameras.Add(camera);
        SaveCameras();
        RebuildGrid();
    }

    void EditCamera(CameraTile tile)
    {
        var dialog = new AddCameraDialog(tile.Camera, isNew: false) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var updated = dialog.Result;
        _cameras[_cameras.FindIndex(c => c.Id == updated.Id)] = updated;
        DisposeTile(updated.Id);
        SaveCameras();
        RebuildGrid();
    }

    void DeleteCamera(CameraTile tile)
    {
        var answer = MessageBox.Show(this, $"¿Eliminar la cámara «{tile.Camera.Name}»?", "CamaraWin",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _cameras.RemoveAll(c => c.Id == tile.Camera.Id);
        DisposeTile(tile.Camera.Id);
        SaveCameras();
        RebuildGrid();
    }

    internal void Notify(string message, string? revealPath)
    {
        StatusText.Text = message;
        _statusRevealPath = revealPath;
    }

    void StatusText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_statusRevealPath is null) return;
        if (Directory.Exists(_statusRevealPath)) Process.Start("explorer.exe", $"\"{_statusRevealPath}\"");
        else if (File.Exists(_statusRevealPath)) Process.Start("explorer.exe", $"/select,\"{_statusRevealPath}\"");
    }

    void OpenRecordings_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        Process.Start("explorer.exe", $"\"{AppPaths.RecordingsDirectory}\"");
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build CamaraWin.slnx`
Expected: `Build succeeded`.

- [ ] **Step 5: Manual check**

With mediamtx and the ffmpeg publisher from Task 11 running, start the app (`dotnet run --project src/CamaraWin.App`) and check:
1. Click "Añade tu primera cámara". Choose "Otra (RTSP)", name "Prueba", main URL `rtsp://127.0.0.1:8554/cam1`, click "Probar": the preview moves. Click "Guardar": the tile appears.
2. Hover the tile, click 📷: the status bar shows `Captura guardada: …png`. Clicking the status text opens Explorer with the file selected, and the PNG is 1280x720.
3. Click ⏺: a red dot appears. Wait 5 s and click ⏹. A `.mkv` in `Vídeos\CamaraWin\Prueba\` plays in VLC or `ffplay`.
4. ✎: rename to "Prueba 2" and save. The tile reconnects with the new name.
5. 🗑: confirm. The tile disappears and the empty state returns.
6. Close and reopen the app: cameras persist. `%AppData%\CamaraWin\cameras.json` has no plaintext password.

- [ ] **Step 6: Commit**

```bash
git add src/CamaraWin.App
git commit -m "feat(app): add/edit/delete cameras, snapshots and recording from tiles

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Drag-to-reorder, fullscreen camera, F11, grid mode and window persistence

**Files:**
- Create: `src/CamaraWin.App/FullscreenWindow.xaml`, `src/CamaraWin.App/FullscreenWindow.xaml.cs`
- Modify: `src/CamaraWin.App/CameraTile.xaml.cs` (mouse and drag/drop overrides), `src/CamaraWin.App/MainWindow.xaml` (grid mode combo, KeyDown), `src/CamaraWin.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `CameraTile` (Tasks 11–12), `GridMode`, `AppSettings`, `SettingsStore`.
- Produces: `CameraTile` events `Action<CameraTile>? FullscreenRequested` and `Action<Guid, Guid>? SwapRequested` (source id, target id); `FullscreenWindow(Camera camera)`.

- [ ] **Step 1: Tile mouse and drag/drop** — add to `CameraTile.xaml.cs` (add `using System.Windows.Input;`):

```csharp
    const string DragFormat = "CamaraWin.CameraId";
    static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    static readonly Brush DropBorder = Brushes.DodgerBlue;
    Point? _dragStart;

    public event Action<CameraTile>? FullscreenRequested;
    public event Action<Guid, Guid>? SwapRequested;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            _dragStart = null;
            FullscreenRequested?.Invoke(this);
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_manage || _dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        DragDrop.DoDragDrop(this, new DataObject(DragFormat, Camera.Id.ToString()), DragDropEffects.Move);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (_manage && e.Data.GetDataPresent(DragFormat)) Frame.BorderBrush = DropBorder;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        Frame.BorderBrush = NormalBorder;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = _manage && e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        Frame.BorderBrush = NormalBorder;
        if (_manage && e.Data.GetData(DragFormat) is string raw && Guid.TryParse(raw, out var source) && source != Camera.Id)
            SwapRequested?.Invoke(source, Camera.Id);
    }
```

- [ ] **Step 2: Fullscreen window** — `src/CamaraWin.App/FullscreenWindow.xaml`

```xml
<Window x:Class="CamaraWin.App.FullscreenWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" ResizeMode="NoResize" WindowState="Maximized"
        WindowStartupLocation="CenterOwner" Background="Black" ShowInTaskbar="False"/>
```

`src/CamaraWin.App/FullscreenWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using CamaraWin.Core;

namespace CamaraWin.App;

public partial class FullscreenWindow : Window
{
    readonly CameraTile _tile;

    public FullscreenWindow(Camera camera)
    {
        InitializeComponent();
        Title = camera.Name;
        _tile = new CameraTile(camera, StreamKind.Main, manage: false);
        _tile.FullscreenRequested += _ => Close();
        _tile.Notify += (message, path) => (Owner as MainWindow)?.Notify(message, path);
        Content = _tile;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Closed += (_, _) => _tile.Dispose();
    }
}
```

- [ ] **Step 3: Main window**

In `MainWindow.xaml`, add `KeyDown="Window_KeyDown"` to the `<Window>` element, and add these items after the "Añadir cámara" button in `MainToolBar`:

```xml
                <Separator/>
                <TextBlock Text="Cuadrícula:" VerticalAlignment="Center" Margin="4,0"/>
                <ComboBox x:Name="GridModeBox" Width="80">
                    <ComboBoxItem Content="Auto"/>
                    <ComboBoxItem Content="1"/>
                    <ComboBoxItem Content="4"/>
                    <ComboBoxItem Content="9"/>
                    <ComboBoxItem Content="16"/>
                </ComboBox>
```

In `MainWindow.xaml.cs` (add `using System.Windows.Controls;` and `using System.Windows.Input;`), replace the constructor, add the members below, and hook the two new tile events in `CreateTile`:

```csharp
    static readonly GridMode[] GridModes = [GridMode.Auto, GridMode.One, GridMode.Four, GridMode.Nine, GridMode.Sixteen];
    WindowState _stateBeforeFullscreen;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        _cameras = [.. _store.Load()];
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;

        RestoreWindowPlacement();
        GridModeBox.SelectedIndex = Math.Max(0, Array.IndexOf(GridModes, _settings.GridMode));
        GridModeBox.SelectionChanged += GridMode_Changed;
        RebuildGrid();
    }

    CameraTile CreateTile(Camera camera)
    {
        var tile = new CameraTile(camera, StreamKind.Sub);
        tile.EditRequested += EditCamera;
        tile.DeleteRequested += DeleteCamera;
        tile.Notify += Notify;
        tile.FullscreenRequested += t => new FullscreenWindow(t.Camera) { Owner = this }.Show();
        tile.SwapRequested += SwapCameras;
        return tile;
    }

    void SwapCameras(Guid source, Guid target)
    {
        var a = _cameras.First(c => c.Id == source);
        var b = _cameras.First(c => c.Id == target);
        (a.Order, b.Order) = (b.Order, a.Order);
        SaveCameras();
        RebuildGrid();
    }

    void GridMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        _settings.GridMode = GridModes[GridModeBox.SelectedIndex];
        RebuildGrid();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None) ToggleFullscreen();
    }

    void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _stateBeforeFullscreen;
            TopBar.Visibility = BottomBar.Visibility = Visibility.Visible;
        }
        else
        {
            _stateBeforeFullscreen = WindowState;
            TopBar.Visibility = BottomBar.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal; // re-maximizing after the style change covers the taskbar
            WindowState = WindowState.Maximized;
        }
    }

    void RestoreWindowPlacement()
    {
        Width = _settings.Width;
        Height = _settings.Height;
        if (_settings.Left is { } left && _settings.Top is { } top
            && left >= SystemParameters.VirtualScreenLeft
            && top >= SystemParameters.VirtualScreenTop
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (WindowStyle == WindowStyle.None) ToggleFullscreen();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Maximized = WindowState == WindowState.Maximized;
        _settingsStore.Save(_settings);
        base.OnClosing(e);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build CamaraWin.slnx`
Expected: `Build succeeded`.

- [ ] **Step 5: Manual check**

Run a second publisher to a different path (`rtsp://127.0.0.1:8554/cam2`, e.g. `testsrc2=size=1280x720` with `-vf hflip`) and add it as a second camera. Then check:
1. Drag tile 1 onto tile 2: the border turns blue on hover, and the tiles swap on drop. After a restart the order is kept.
2. Double-click a tile: fullscreen at 1280x720 appears in about 1 s. Esc or a double-click closes it, and the grid tile keeps playing without reconnecting.
3. F11 hides the toolbar and status bar and covers the taskbar. F11 or Esc restores.
4. Cuadrícula = 1 shows only the first camera, and Auto shows both.
5. Move and resize the window, close it, reopen: position and size are restored.

- [ ] **Step 6: Commit**

```bash
git add src/CamaraWin.App
git commit -m "feat(app): drag-to-reorder, fullscreen camera, F11 and grid modes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: ONVIF discovery dialog

**Files:**
- Create: `src/CamaraWin.App/DiscoveryDialog.xaml`, `src/CamaraWin.App/DiscoveryDialog.xaml.cs`
- Modify: `src/CamaraWin.App/MainWindow.xaml` (toolbar button), `src/CamaraWin.App/MainWindow.xaml.cs` (handler)

**Interfaces:**
- Consumes: `WsDiscovery.ProbeAsync`, `DiscoveredDevice`, `OnvifClient` (+ `CreateHttpClient`, `GetDeviceInformationAsync`, `ResolveStreamUrisAsync`), `OnvifException`, `OnvifAuthException`, `BrandInference`; `MainWindow.AddCamera(Camera? prefilled)` (Task 12).
- Produces: `DiscoveryDialog(IEnumerable<string> knownHosts)` with `Camera? Result`.

- [ ] **Step 1: Dialog** — `src/CamaraWin.App/DiscoveryDialog.xaml`

```xml
<Window x:Class="CamaraWin.App.DiscoveryDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Buscar cámaras en la red" Width="620" Height="460"
        WindowStartupLocation="CenterOwner">
    <DockPanel Margin="16">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button x:Name="SearchButton" Content="🔍 Buscar de nuevo" Padding="10,4" Click="Search_Click"/>
            <TextBlock x:Name="StatusText" VerticalAlignment="Center" Margin="12,0,0,0" TextWrapping="Wrap"/>
        </StackPanel>
        <StackPanel DockPanel.Dock="Bottom" Margin="0,8,0,0">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Usuario" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <TextBox x:Name="UserBox" Grid.Column="1" Margin="0,0,12,0"/>
                <TextBlock Grid.Column="2" Text="Contraseña" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <PasswordBox x:Name="PasswordInput" Grid.Column="3"/>
            </Grid>
            <TextBlock Foreground="Gray" TextWrapping="Wrap" Margin="0,6,0,0"
                       Text="Tapo: la «Cuenta de cámara». Imou: admin + código de seguridad."/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
                <Button x:Name="AddButton" Content="Añadir seleccionada" Padding="10,4" Margin="0,0,8,0" IsDefault="True" Click="Add_Click"/>
                <Button Content="Cerrar" Padding="10,4" IsCancel="True"/>
            </StackPanel>
        </StackPanel>
        <ListView x:Name="DeviceList">
            <ListView.View>
                <GridView>
                    <GridViewColumn Header="IP" Width="130" DisplayMemberBinding="{Binding Host}"/>
                    <GridViewColumn Header="Nombre" Width="170" DisplayMemberBinding="{Binding Name}"/>
                    <GridViewColumn Header="Modelo" Width="140" DisplayMemberBinding="{Binding Hardware}"/>
                    <GridViewColumn Header="Estado" Width="110" DisplayMemberBinding="{Binding Status}"/>
                </GridView>
            </ListView.View>
        </ListView>
    </DockPanel>
</Window>
```

`src/CamaraWin.App/DiscoveryDialog.xaml.cs`:

```csharp
using System.Windows;
using CamaraWin.Core;
using CamaraWin.Core.Onvif;

namespace CamaraWin.App;

public partial class DiscoveryDialog : Window
{
    readonly HashSet<string> _knownHosts;

    public DiscoveryDialog(IEnumerable<string> knownHosts)
    {
        InitializeComponent();
        _knownHosts = new HashSet<string>(knownHosts, StringComparer.OrdinalIgnoreCase);
        Loaded += async (_, _) => await SearchAsync();
    }

    public Camera? Result { get; private set; }

    public sealed record Row(DiscoveredDevice Device, string Host, string Name, string Hardware, string Status);

    async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    async Task SearchAsync()
    {
        SearchButton.IsEnabled = false;
        StatusText.Text = "Buscando cámaras ONVIF en la red…";
        try
        {
            var devices = await WsDiscovery.ProbeAsync(TimeSpan.FromSeconds(3));
            DeviceList.ItemsSource = devices
                .Select(d => new Row(d, d.Host, d.Name ?? "", d.Hardware ?? "", _knownHosts.Contains(d.Host) ? "Ya añadida" : ""))
                .ToList();
            StatusText.Text = devices.Count == 0
                ? "No se encontró ninguna. Puede que no tengan ONVIF activado: añádelas a mano."
                : $"{devices.Count} dispositivo(s) encontrados.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error al buscar: {ex.Message}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not Row row)
        {
            StatusText.Text = "Selecciona una cámara de la lista.";
            return;
        }
        AddButton.IsEnabled = false;
        StatusText.Text = "Consultando la cámara…";
        try
        {
            var user = UserBox.Text.Trim();
            var password = PasswordInput.Password;
            using var http = OnvifClient.CreateHttpClient(user, password);
            var client = new OnvifClient(http, new Uri(row.Device.DeviceServiceUrl), user, password);

            OnvifDeviceInfo? info = null;
            try { info = await client.GetDeviceInformationAsync(); }
            catch (OnvifException ex) when (ex is not OnvifAuthException) { }

            var (main, sub) = await client.ResolveStreamUrisAsync();
            Result = new Camera
            {
                Name = DisplayName(info, row),
                Brand = BrandInference.FromManufacturer(info?.Manufacturer ?? row.Device.Name),
                Host = row.Host,
                Port = Uri.TryCreate(main, UriKind.Absolute, out var uri) && uri.Port > 0 ? uri.Port : 554,
                User = user,
                Password = password,
                MainUrlOverride = main,
                SubUrlOverride = sub,
            };
            DialogResult = true;
        }
        catch (OnvifAuthException)
        {
            StatusText.Text = "Usuario o contraseña incorrectos.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo consultar la cámara: {ex.Message}";
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    static string DisplayName(OnvifDeviceInfo? info, Row row) =>
        info?.Model is { Length: > 0 } model ? $"{model} ({row.Host})"
        : row.Name.Length > 0 ? $"{row.Name} ({row.Host})"
        : row.Host;
}
```

- [ ] **Step 2: Wire into main window**

In `MainWindow.xaml`, add after the "Añadir cámara" button:

```xml
                <Button Content="🔍 Buscar en red" Click="Discover_Click"/>
```

In `MainWindow.xaml.cs`:

```csharp
    void Discover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiscoveryDialog(_cameras.Select(c => c.Host)) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } found) AddCamera(found);
    }
```

`AddCamera(found)` opens the add dialog prefilled, so the user can rename the camera and test it before saving.

- [ ] **Step 3: Build**

Run: `dotnet build CamaraWin.slnx`
Expected: `Build succeeded`.

- [ ] **Step 4: Manual check (real network)**

Run the app on the LAN where the cameras are and click "Buscar en red":
1. Tapo cameras with a "Cuenta de cámara" set should appear (ONVIF port 2020). Imou cameras appear only if ONVIF is enabled.
2. Select one, enter credentials, click "Añadir seleccionada": the add dialog opens prefilled with the brand, IP and ONVIF URLs (under Avanzado). "Probar" shows video, and "Guardar" adds it.
3. Wrong password gives "Usuario o contraseña incorrectos.".
4. Cameras already added show "Ya añadida".

If Windows Firewall blocks the UDP replies, the list is empty. Report it to the user; changing firewall rules is their decision.

- [ ] **Step 5: Commit**

```bash
git add src/CamaraWin.App
git commit -m "feat(app): ONVIF network discovery dialog

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: README, licenses and acceptance check with the real cameras

**Files:**
- Create: `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md`

- [ ] **Step 1: LICENSE** (MIT)

```
MIT License

Copyright (c) 2026 CamaraWin contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: THIRD-PARTY-NOTICES.md**

```markdown
# Third-party software

## FFmpeg
CamaraWin loads FFmpeg (https://ffmpeg.org) as separate shared libraries (`ffmpeg\*.dll`).
The bundled build is the **LGPL v2.1+** variant from https://github.com/BtbN/FFmpeg-Builds.
FFmpeg's source code is available at https://ffmpeg.org/download.html. You may replace the DLLs
with your own compatible build (FFmpeg 9.0, avcodec-63).

## FFmpeg.AutoGen
C# bindings for FFmpeg, LGPL-3.0: https://github.com/Ruslan-B/FFmpeg.AutoGen

## mediamtx (tests only, not distributed)
MIT License: https://github.com/bluenviron/mediamtx
```

- [ ] **Step 3: README.md** (Spanish)

````markdown
# CamaraWin

Visor de cámaras IP para Windows con **latencia mínima**. Muestra cámaras **TP-Link Tapo**,
**Imou** y cualquier cámara **RTSP** en cuadrícula, sin pasar por la nube.

- Imagen en menos de 1 s y ~100–300 ms de retardo en red local (FFmpeg sin búfer + GPU).
- Añadir cámaras a mano o **buscarlas en la red** (ONVIF).
- Doble clic: pantalla completa en calidad alta. Arrastrar: reordenar.
- 📷 Capturas PNG a resolución completa · ⏺ Grabación MKV sin recodificar.
- Contraseñas cifradas con DPAPI de Windows.

## Preparar las cámaras

| Marca | Qué hacer | URL que usa la app |
|---|---|---|
| Tapo | App Tapo › cámara › Ajustes avanzados › **Cuenta de cámara** (usuario/contraseña) | `rtsp://IP:554/stream1` (alta) · `/stream2` (baja) |
| Imou | Usuario `admin`, contraseña = **código de seguridad** de la pegatina. En algunos modelos hay que activar RTSP/ONVIF en la app Imou | `rtsp://IP:554/cam/realmonitor?channel=1&subtype=0` · `subtype=1` |

## Compilar

Requisitos: Windows 10/11, .NET SDK 10.

```powershell
pwsh -File tools/get-ffmpeg.ps1      # descarga FFmpeg 9.0 LGPL (≈ 80 MB)
dotnet run --project src/CamaraWin.App
```

Pruebas:

```powershell
dotnet test tests/CamaraWin.Core.Tests
pwsh -File tools/get-mediamtx.ps1    # servidor RTSP para las pruebas de vídeo
dotnet test tests/CamaraWin.Media.Tests   # necesita ffmpeg/ffprobe (con libx264) en el PATH
```

## Datos

- Cámaras: `%AppData%\CamaraWin\cameras.json` (contraseñas cifradas, solo tu usuario de Windows puede leerlas)
- Grabaciones: `Vídeos\CamaraWin\<cámara>\`
- Capturas: `Imágenes\CamaraWin\`

## Licencia

MIT. Ver [LICENSE](LICENSE) y [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) (FFmpeg es LGPL).
````

- [ ] **Step 4: Full test run**

Run: `dotnet test CamaraWin.slnx`
Expected: all tests PASS (Media integration tests may be skipped only if mediamtx or ffmpeg is missing; report which ones).

- [ ] **Step 5: Acceptance with the user's cameras (with the user)**

Ask the user to add their 3 Tapo + 4 Imou cameras (manually or via "Buscar en red"), then verify together:
1. All 7 tiles show video within ~1 s of launch (3x3 Auto grid).
2. Latency: point a camera at a phone showing a stopwatch next to the monitor and take a photo of both. Expect ≤ 300 ms on LAN with TCP. Try "Usar UDP" on one camera and compare.
3. Task Manager: CPU stays low with 7 tiles, and the GPU "Video Decode" engine is active.
4. Unplug one camera for 30 s: its tile shows "Reconectando…" and recovers by itself.
5. Double-click fullscreen, snapshot and record on one Tapo and one Imou. The PNG is at full resolution, and the MKV plays and is full resolution.
6. If a camera fails with a password containing special characters (`@ : / %`), note it. The fix would be in `StreamUrlBuilder` escaping.

Record the measured latency and any failing model in the README "Preparar las cámaras" section if relevant.

- [ ] **Step 6: Commit**

```bash
git add README.md LICENSE THIRD-PARTY-NOTICES.md
git commit -m "docs: README, MIT license and third-party notices

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
