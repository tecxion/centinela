# CamaraWin — Design Spec

Date: 2026-09-26
Status: Approved (design), pending spec review

## 1. Goal

Windows desktop app to view IP cameras (TP-Link Tapo and Imou/Dahua, plus generic RTSP)
with **instant start and minimum latency**. Personal use first; later open source (MIT).

Initial setup: 3 Tapo + 4 Imou cameras. Camera list is dynamic (add/edit/remove).

### Success criteria

- Stream visible < 1 s after the app starts / a camera is added.
- Glass-to-glass latency ~100–300 ms on LAN (vs 1–3 s in vendor apps).
- 7 substreams + 1 fullscreen mainstream with low CPU (GPU decode).
- Recording and snapshots do not add latency to live view.

### In scope (v1)

1. Grid view of all cameras (auto layout) using substreams.
2. Add camera manually (brand, IP, user, password) + custom RTSP URL ("Other").
3. Discover cameras on LAN via ONVIF WS-Discovery.
4. Double-click tile → fullscreen with mainstream.
5. Drag-and-drop to reorder tiles (persisted).
6. Snapshot (PNG, native resolution).
7. Manual recording per camera (start/stop button), MKV, no re-encode.

### Out of scope (v1)

PTZ (planned next), audio, 24/7 recording, motion detection, cloud APIs.

## 2. Tech stack

- **C# / .NET 10**, **WPF** (Windows only).
- **FFmpeg via FFmpeg.AutoGen** — direct control of buffering (no player clock).
- FFmpeg **LGPL shared** DLLs (BtbN build `ffmpeg-n9.0-latest-win64-lgpl-shared-9.0`) next to the
  exe; fetched with `tools/get-ffmpeg.ps1`. **FFmpeg.AutoGen 9.0.1.1** (expects avcodec-63,
  avformat-63, avutil-61, swscale-10). Package and DLL majors must match.
- xUnit for tests.
- License: MIT for app code; FFmpeg LGPL (dynamically linked, DLLs shipped separately).

### Render approach (decided)

Decode on GPU (D3D11VA) → `av_hwframe_transfer_data` to system memory →
`sws_scale` to BGRA at tile size → WPF `WriteableBitmap`.
Extra cost ~1–3 ms/frame; allows WPF overlays on video. Rendering sits behind an
interface (`IFrameSink`) so a zero-copy D3D11 path can replace it later.

## 3. Solution layout

```
CamaraWin.sln
├─ src/CamaraWin.Core        (net10.0, no UI, no FFmpeg)
│   ├─ Camera, Brand          model
│   ├─ StreamUrlBuilder       Tapo / Imou / Custom URL generation
│   ├─ CameraStore            JSON persistence, DPAPI-encrypted passwords
│   ├─ GridLayout             camera count → rows/cols
│   └─ Onvif/
│       ├─ WsDiscovery        UDP multicast probe
│       └─ OnvifClient        GetDeviceInformation, GetProfiles, GetStreamUri (WS-UsernameToken)
├─ src/CamaraWin.Media       (net10.0-windows, FFmpeg.AutoGen)
│   ├─ StreamSession          open/read/decode loop, reconnect, one thread per stream
│   ├─ FrameMailbox           single-slot latest-frame buffer
│   ├─ Recorder               packet remux to MKV
│   └─ SnapshotWriter         last decoded frame → PNG at native resolution
├─ src/CamaraWin.App         (net10.0-windows, WPF)
│   ├─ MainWindow             toolbar + grid
│   ├─ CameraTile             video + overlays + drag/drop
│   ├─ FullscreenWindow
│   ├─ AddCameraDialog
│   └─ DiscoveryDialog
├─ tests/CamaraWin.Core.Tests
├─ tests/CamaraWin.Media.Tests (integration, uses local RTSP test server)
└─ tools/get-ffmpeg.ps1
```

## 4. Data model & persistence

```csharp
enum Brand { Tapo, Imou, Custom }

class Camera {
  Guid Id; string Name; Brand Brand;
  string Host; int Port = 554;
  string User; string Password;          // plaintext only in memory
  string? MainUrlOverride; string? SubUrlOverride;  // Custom or from ONVIF
  bool UseUdp = false;
  int Order;
}
```

- File: `%AppData%\CamaraWin\cameras.json`.
- Password stored as base64 of `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`.
  Never written in plaintext; never logged.
- Credentials are URL-encoded when embedded in the RTSP URL.
- Window settings (size, position, grid mode) in `%AppData%\CamaraWin\settings.json`.

### URL rules (`StreamUrlBuilder`)

| Brand | Main | Sub |
|---|---|---|
| Tapo | `rtsp://u:p@host:port/stream1` | `.../stream2` |
| Imou | `rtsp://u:p@host:port/cam/realmonitor?channel=1&subtype=0` | `...subtype=1` |
| Custom | `MainUrlOverride` | `SubUrlOverride ?? MainUrlOverride` |

Overrides (e.g. from ONVIF) win over brand rules for any brand.
Imou default user: `admin`; password = device safety code.

## 5. Streaming pipeline (`StreamSession`)

One dedicated thread per session. Options passed to `avformat_open_input`:

```
rtsp_transport     = tcp   (udp if Camera.UseUdp)
fflags             = nobuffer
probesize          = 32768
max_delay          = 0
reorder_queue_size = 0
timeout            = 5000000 (µs, socket I/O)
```

Live-view sessions **skip `avformat_find_stream_info`** (it waits for frames; `analyzeduration=0`
means "default 5 s" in FFmpeg). Codec id and extradata come from the RTSP SDP; the decoder
learns size from the bitstream. Recording sessions (no decode) do call it, to get width/height
for the MKV header.

Loop:
1. `av_read_frame` → packet.
2. (Recording sessions only) hand packet to `Recorder` (clone).
3. Decode (D3D11VA hw device; fall back to software decode on failure, silently).
4. `av_hwframe_transfer_data` → keep as `lastFrame` (native res, for snapshots).
5. `sws_scale` to BGRA at current target size (set by UI when tile resizes).
6. `FrameMailbox.Put(buffer)` — overwrites; no queue; stale frames dropped.

Codec context: `AV_CODEC_FLAG_LOW_DELAY`, `thread_count` 1–2 (frame threading adds latency;
use slice threading only).

### UI presentation

Each `CameraTile` subscribes to `CompositionTarget.Rendering`. On each tick, if the mailbox
has a new frame, copy it into its `WriteableBitmap` (`Lock / memcpy / AddDirtyRect / Unlock`).
No PTS scheduling, no A/V clock. Double buffers in the mailbox avoid allocations.

### Fullscreen

Double-click opens `FullscreenWindow` with a **second** session on the mainstream.
Grid session stays alive, so returning is instant. Exit with Esc or double-click.

### Error handling & states

Session state: `Connecting`, `Playing`, `Reconnecting`, `AuthFailed`, `Stopped`.

- Read error or no packet for 5 s → `Reconnecting`, backoff 1 s, 2 s, 4 s, max 10 s,
  retry forever. Tile shows last frame dimmed + "Reconectando…".
- 401 / auth error → `AuthFailed`, no retry; tile shows "Credenciales incorrectas" + edit.
- Stop is cooperative (cancellation token + FFmpeg interrupt callback so blocking reads abort
  immediately).

## 6. Recording (`Recorder`)

- Manual start/stop per tile.
- **Always records the mainstream** through a separate headless session (`decode: false`,
  near-zero CPU), so grid recordings are full quality. Costs one extra RTSP connection per
  recording camera. On reconnect a new file is started.
- Output: `%USERPROFILE%\Videos\CamaraWin\<CameraName>\yyyy-MM-dd_HH-mm-ss.mkv`
  (camera name sanitized for filesystem).
- Remux only: copy codec parameters from input stream, `av_interleaved_write_frame`.
  Starts at the next keyframe. Timestamps rebased to start at 0.
- Runs on its own thread fed by a bounded packet queue so disk I/O never blocks decode;
  if queue overflows, recording is stopped with an error (live view unaffected).
- MKV chosen: still playable if the app crashes.
- Video only (audio out of scope).

## 7. Snapshots

- Button on tile (and in fullscreen).
- In fullscreen: from the mainstream session's `lastFrame`. In the grid: a temporary mainstream
  session grabs one frame (~1 s), falling back to the substream frame on timeout.
- Encoded with FFmpeg's PNG encoder (RGB24) and saved to
  `%USERPROFILE%\Pictures\CamaraWin\<CameraName>_yyyy-MM-dd_HH-mm-ss.png`.
- Brief toast "Captura guardada" with click-to-open-folder.

## 8. UI

**Main window toolbar:** Añadir cámara · Buscar en red · Cuadrícula (Auto/1/4/9/16) ·
Abrir carpeta de grabaciones.

**Grid:** in Auto, `GridLayout` computes cols = ceil(sqrt(n)), rows = ceil(n/cols).
Fixed modes (1/4/9/16) show the first N cameras by `Order` (no pagination in v1);
sessions for hidden cameras are stopped.
Empty grid shows a call-to-action "Añade tu primera cámara".

**Tile:** video (Uniform stretch, black bars), name + state label, red ● when recording.
On hover: 📷 snapshot, ⏺ record, ✎ edit, 🗑 delete (confirm dialog).
Drag tile onto another → swap `Order`, persist.

**Window:** F11 toggles borderless fullscreen; size/position persisted.

**AddCameraDialog:** Brand (Tapo/Imou/Otra), Name, IP, Port (554), User, Password
(Imou prefills `admin`), for "Otra": main/sub URL fields, UDP checkbox.
"Probar" button opens a temporary session and shows a live preview + result.

## 9. ONVIF discovery

1. `WsDiscovery.ProbeAsync(timeout: 3 s)` — SOAP Probe for
   `dn:NetworkVideoTransmitter` to `239.255.255.250:3702` on every active IPv4 interface;
   parse `ProbeMatch` → XAddrs.
2. List results: IP, manufacturer/model (via `GetDeviceInformation`, may require auth),
   marked if already added (same host).
3. User selects one + enters credentials → `GetProfiles` + `GetStreamUri` → main (highest
   res profile) and sub (lowest res) URLs stored as overrides.
4. Brand inferred: manufacturer contains "TP-Link"/"Tapo" → Tapo; "Dahua"/"Imou" → Imou;
   else Custom.
5. Auth: WS-UsernameToken with PasswordDigest. Minimal hand-written SOAP (HttpClient +
   XDocument) — no WCF dependency.

Cameras not answering ONVIF are added manually.

## 10. Testing

- **Core unit tests (xUnit):** StreamUrlBuilder (all brands, overrides, special chars in
  password), CameraStore (round-trip, password not in plaintext on disk), GridLayout,
  WS-Discovery ProbeMatch parsing and ONVIF GetStreamUri/GetProfiles parsing from sample XML,
  brand inference.
- **Media integration tests:** local RTSP server (mediamtx) fed by `ffmpeg -re -stream_loop -1`
  test pattern: session connects and produces frames; recorder writes a valid MKV
  (verified with ffprobe); session reconnects after server restart.
  Skipped automatically if FFmpeg DLLs / mediamtx are missing.
- **Manual:** the user's 7 cameras — latency check with a stopwatch on screen.

## 11. Future (not v1)

PTZ via ONVIF, audio for the selected camera, 24/7 recording with rotation, zero-copy D3D11
render path, motion detection, installer / GitHub Releases.
