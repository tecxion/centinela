# CamaraWin v1.1 — Featured layout + translated connection errors

Date: 2026-09-26
Status: Approved (design), pending spec review
Base: CamaraWin v1 (docs/superpowers/specs/2026-09-26-camara-win-design.md)

## 1. Goals

1. New layout "Principal + miniaturas": one large featured camera plus the rest as thumbnails
   around it (L-shape). Click a thumbnail to feature it.
2. Understandable connection errors: classify failures, stop showing a bare "Reconectando…",
   show non-blocking Spanish toasts with advice, and keep an error log (window + files).
   Wrong passwords must be detected even when the camera does not return a clean 401.

Out of scope: PTZ, audio, 24/7 recording (unchanged from v1).

## 2. Featured layout

### Settings
- `AppSettings.LayoutMode` enum `{ Grid, Featured }` (default `Grid`; the existing `GridMode` keeps
  meaning for `Grid`).
- `AppSettings.FeaturedCameraId: Guid?`. If null or the camera no longer exists → first camera by
  `Order`.
- UI: the "Cuadrícula" combo gains a last entry "Principal + miniaturas" (selecting it sets
  `LayoutMode = Featured`; any other entry sets `LayoutMode = Grid` + that `GridMode`).

### Geometry (Core, `GridLayout.ComputeFeatured(int cameraCount)`)
- n = 0 → nothing (empty state). n = 1 → featured fills the window (1x1).
- n ≥ 2 → k = max(1, ceil((n − 2) / 2)); grid is (k+1)×(k+1); featured spans rows 0..k−1,
  cols 0..k−1; thumbnail slots, in order: right column top→bottom (rows 0..k−1, col k), then
  bottom row left→right (row k, cols 0..k). Capacity 2k+1 ≥ n−1.
- Returns `FeaturedLayout(int Size, int FeaturedSpan, IReadOnlyList<(int Row, int Col)> Slots)`
  with exactly n−1 slots used.
- Examples: n=2 → 2x2, span 1, slots (0,1). n=7 → 4x4, span 3, 6 slots
  (0,3),(1,3),(2,3),(3,0),(3,1),(3,2). n=8 → 4x4, 7 slots (adds (3,3)). n=9 → 5x5 (k=4).
- Thumbnails are ordered by camera `Order`, skipping the featured camera.
- All cameras stay visible in this mode (no hidden cameras → recordings are never stopped by
  switching to it).

### Rendering and streams
- The main window uses a `Grid` (row/column definitions of equal star size) instead of
  `UniformGrid` in Featured mode.
- Featured tile uses the **Main** stream, thumbnails the **Sub** stream.
- Changing the featured camera must not show black: the new featured camera keeps its current
  Sub tile visible (stretched) until its new Main tile has published its first frame, then the
  Sub tile is swapped out and shut down; the previously featured camera gets a new Sub tile.
  (Implementation: `CameraTile` exposes `FirstFrameShown` event/Task.)
- Tiles keep their identity per (camera, stream kind); moving between cells does not reconnect.

### Interaction
- Single click (mouse up without exceeding the drag threshold, not a double-click) on a thumbnail
  → it becomes featured; `FeaturedCameraId` persisted immediately.
- Double-click → fullscreen (unchanged). Drag → reorder by swapping `Order` (unchanged; applies
  to thumbnails; dropping onto the featured tile makes the dragged camera featured).
- Clicking the featured tile does nothing.

## 3. Connection errors

### Classification (Media)
```csharp
public enum StreamErrorKind { AuthFailed, Unreachable, NotFound, CameraBusy, ServerError, Stalled, Unknown }
public sealed record StreamError(StreamErrorKind Kind, int? RtspStatus, int FfmpegCode, string Detail, DateTime TimeUtc);
```
- `StreamSession.ErrorOccurred` event (session thread; subscriber exceptions isolated), raised on
  every failed connection attempt / stall. `LastError` stays (now the sanitized Detail).
- `StreamSession.LastErrorKind` property.
- Source of truth, in order:
  1. RTSP status parsed from FFmpeg log lines for this connection (e.g. `method DESCRIBE failed:
     401 Unauthorized`, `... 404 Not Found`, `453 Not Enough Bandwidth`, `503 Service Unavailable`).
     Captured with `av_log_set_callback`; lines are routed to the owning session by matching the
     log context pointer against the session's `AVFormatContext*` (registry of live contexts).
     Unmatched lines are ignored. FFmpeg's default stderr printing is suppressed.
  2. FFmpeg error code: AVERROR_HTTP_UNAUTHORIZED/FORBIDDEN/EACCES → AuthFailed;
     AVERROR_HTTP_NOT_FOUND → NotFound; AVERROR_HTTP_SERVER_ERROR → ServerError (503 → CameraBusy
     when status known); ETIMEDOUT, ECONNREFUSED, EHOSTUNREACH, ENETUNREACH, EADDRNOTAVAIL →
     Unreachable; interrupt from the 5 s stall deadline while already Playing → Stalled; while
     opening → Unreachable.
  3. Otherwise Unknown.
- Pure classification function `StreamErrorClassifier.Classify(int ffmpegCode, int? rtspStatus,
  bool wasPlaying, bool deadlineHit)` — unit tested.
- RTSP status → kind: 401/403 → AuthFailed; 404/454 → NotFound; 453/503 → CameraBusy;
  other 5xx → ServerError.
- `Detail` = operation + av_strerror text + RTSP status line if any; every `scheme://user:pass@`
  is replaced by `scheme://` (sanitizer unit-tested). Never contains credentials.

### Retry policy
- AuthFailed → state `AuthFailed`, no retry (unchanged).
- NotFound, CameraBusy → retry every 30 s.
- Others → current backoff 1, 2, 4, 8, 10 s.

### Translation (Core, `ErrorTranslator`)
`Translate(StreamErrorKind kind, Brand brand, string cameraName) → TranslatedError(string Short,
string Title, string Advice)`. Spanish, brand-aware. Short is for the tile (≤ 30 chars).

| Kind | Short | Advice (brand-specific where noted) |
|---|---|---|
| AuthFailed | Contraseña incorrecta | Tapo: usa la «Cuenta de cámara» (app Tapo › Ajustes avanzados), no tu cuenta TP-Link. Imou: usuario admin y el código de seguridad de la pegatina. Otra: revisa usuario y contraseña. |
| Unreachable | Sin conexión | Comprueba que la cámara está encendida y en la misma red, y que la IP no ha cambiado (reserva la IP en el router). |
| NotFound | Vídeo no disponible | Imou: activa RTSP/ONVIF en la app Imou. Tapo: crea la «Cuenta de cámara». Otra: revisa la URL RTSP. |
| CameraBusy | Cámara ocupada | Demasiadas conexiones a la vez: cierra la app oficial, la pantalla completa o una grabación. |
| ServerError | Error de la cámara | Reinicia la cámara. Si continúa, actualiza su firmware. |
| Stalled | Imagen congelada | La cámara dejó de enviar vídeo; se reconectará sola. Revisa la cobertura Wi-Fi. |
| Unknown | Error de conexión | Consulta el detalle técnico en el registro. |

### Toasts (App)
- In-window overlay, bottom-right, stacked (max 4 visible, oldest dropped), auto-hide 8 s, click
  → opens the log window; × closes.
- One toast per (camera, kind) until that camera reaches Playing again.
- AuthFailed, NotFound, CameraBusy: toast on first occurrence. Unreachable, Stalled, ServerError,
  Unknown: toast after 3 consecutive failed attempts.
- On recovery after a toast was shown: short toast "✓ <cámara>: conexión recuperada" (auto-hide 4 s).
- Only live-view sessions (grid/featured/fullscreen tiles) and recording sessions produce toasts;
  the Add dialog's test session and the snapshot temp session do not (errors shown inline /
  status bar as today).

### Tile
- Status label shows `TranslatedError.Short` instead of "Reconectando…" once an error is known
  (while retrying: "Sin conexión · reintentando").
- AuthFailed: an always-visible "Editar" button in the tile (not only on hover).

### Error log
- `ErrorLog` service (App): ring buffer of the last 500 entries
  `(TimeLocal, CameraName, Kind, Title, Detail)`; append each entry as one line to
  `%AppData%\CamaraWin\logs\camarawin-yyyy-MM-dd.log` (UTF-8, tab-separated); on startup delete
  log files older than 14 days. File I/O on a background queue; failures ignored.
- Recoveries are logged too (Kind column "Recuperada").
- Status bar: button "Registro (N)" where N = entries since the window was last opened.
- Log window: DataGrid (Hora, Cámara, Error, Detalle), newest first; buttons Copiar (selected
  rows as text), Abrir carpeta, Limpiar (clears the in-memory list only).

## 4. Testing
- Core: `GridLayout.ComputeFeatured` (n = 0,1,2,3,7,8,9,10), `ErrorTranslator` (every kind × brand
  returns non-empty Spanish text; Short ≤ 30 chars), settings round-trip of LayoutMode/FeaturedCameraId.
- Media: `StreamErrorClassifier` table tests; RTSP status-line parser tests with real FFmpeg
  log strings; credential sanitizer tests; integration (mediamtx): wrong password → AuthFailed +
  ErrorOccurred with RtspStatus 401; unknown path → NotFound; unreachable port → Unreachable;
  Detail never contains the password.
- App: build + smoke run; manual check with the user's cameras (wrong password on one Tapo and
  one Imou must show "Contraseña incorrecta" and a toast).
