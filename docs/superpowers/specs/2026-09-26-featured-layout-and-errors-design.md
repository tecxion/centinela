# CamaraWin v1.1 — Featured layout, translated errors, menu/backup, tray, record-all, stats

Date: 2026-09-26
Status: Approved (design), pending spec review
Base: CamaraWin v1 (docs/superpowers/specs/2026-09-26-camara-win-design.md)

## 1. Goals

1. New layout "Principal + miniaturas": one large featured camera plus the rest as thumbnails
   around it (L-shape). Click a thumbnail to feature it.
2. Understandable connection errors: classify failures, stop showing a bare "Reconectando…",
   show non-blocking Spanish toasts with advice, and keep an error log (window + files).
   Wrong passwords must be detected even when the camera does not return a clean 401.
3. Menu bar (Archivo, Ver) with JSON import/export (optionally password-encrypted), automatic
   backup, in-app manual, license and support windows.
4. System tray (close-to-tray, start with Windows), "Grabar todas", per-tile stats overlay.

Out of scope: PTZ (next version), audio, 24/7 recording.

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

## 4. Menu bar and JSON backup

### Menu
A WPF `Menu` above the toolbar.
- **Archivo**: Importar JSON… · Exportar JSON… · Carpeta de copia automática… · separator ·
  Manual (README) · Licencia · Soporte · separator · Salir.
- **Ver**: Mostrar estadísticas (checkable, persisted `AppSettings.ShowStats`).
- Salir = real exit (same shutdown path as v1's window close: recordings finalized).

### JSON format (Core, `CameraBackup`)
```json
{
  "format": "camarawin-cameras",
  "version": 1,
  "exportedAt": "2026-09-26T21:40:00Z",
  "encryption": { "algorithm": "AES-256-GCM", "kdf": "PBKDF2-SHA256", "iterations": 600000, "salt": "<base64 16 bytes>" },
  "cameras": [
    { "name": "Garaje", "brand": "Tapo", "host": "192.168.1.20", "port": 554, "user": "camuser",
      "password": "<base64(nonce 12 | ciphertext | tag 16)>", "mainUrl": null, "subUrl": null, "useUdp": false }
  ]
}
```
- Without passwords: `"encryption": null`, every `"password": null`.
- Key = PBKDF2-SHA256(passphrase UTF-8, salt, 600000 iterations, 32 bytes). Each password is
  encrypted independently with AES-GCM (random 12-byte nonce), associated data = UTF-8 of
  `name|host|port|mainUrl|subUrl` (null → empty) so entries cannot be swapped and override URLs
  cannot be redirected to another host without failing authentication.
- `Order` is the array order; camera `Id` is not exported (new ids on import). Override URLs are
  exported without credentials (v1 already strips them).
- API: `string Export(IEnumerable<Camera>, string? passphrase)`;
  `BackupImport Import(string json, Func<string?> askPassphrase)` → cameras + counts; errors:
  `BackupFormatException` (not our format / corrupt / unknown future version, Spanish message),
  `BackupPassphraseException` (wrong passphrase — detected by GCM tag failure).
- Example file committed at `docs/ejemplo-camaras.json` (encryption null, two cameras: one Tapo,
  one Imou, fake IPs, password null).

### Import (App)
- File dialog → if encrypted, passphrase prompt (retry on wrong key, cancel aborts).
- Merge (pure Core function): same host (case-insensitive) + port as an existing camera → update
  that camera's fields (keep its Id, Order and password unless the import carries one); otherwise
  append at the end.
- Result message: "N añadidas, M actualizadas, K sin contraseña".
- Cameras without password show tile state "Falta contraseña" + always-visible Editar button
  (same treatment as AuthFailed; they are still attempted — some cameras allow empty passwords).

### Export (App)
- Dialog: checkbox "Incluir contraseñas (cifradas con una clave)"; if checked, passphrase +
  confirmation (min 8 chars, must match). Then save-file dialog, default name
  `camarawin-camaras-yyyy-MM-dd.json`.

### Automatic backup
- After every camera-list change (add, edit, delete, reorder, import) write
  `<BackupFolder>\camaras-copia.json` without passwords, atomically (temp + move), on a background
  task; failures → status bar message, never an exception.
- `AppSettings.BackupFolder` (default `%USERPROFILE%\Documents\CamaraWin`); "Carpeta de copia
  automática…" opens `Microsoft.Win32.OpenFolderDialog` and writes a fresh copy immediately.

### Manual / Licencia / Soporte windows
- Manual: scrollable window with Spanish text (FlowDocument, embedded resource): añadir cámaras
  (Tapo/Imou/Otra/Buscar en red), vistas (cuadrícula, principal + miniaturas, pantalla completa,
  F11), grabar y capturas (dónde se guardan), bandeja del sistema, copias JSON, errores frecuentes
  (the ErrorTranslator table in prose), note that true camera-to-screen latency is not measurable.
- Licencia: plain-language explanation of MIT (use, copy, modify, distribute, sell; keep the
  notice; no warranty) + full MIT text + note that FFmpeg is LGPL and its DLLs can be replaced.
- Soporte: text + clickable links https://www.tecxart.es and mailto:tecxart@gmail.com (opened
  with the shell).

## 5. System tray

- `System.Windows.Forms.NotifyIcon` (`UseWindowsForms` in the App project, no extra packages).
- Window close (X) → hide window, show tray icon; first time only, balloon "CamaraWin sigue en la
  bandeja". Persist `AppSettings.TrayHintShown`.
- While hidden: all live-view tiles are shut down (recording sessions keep running); on show, the
  view is rebuilt and reconnects (~1 s).
- Tray icon: app icon; variant with a red dot while any recording is active.
- Tray menu: Abrir · Grabar todas / Detener todas · Arrancar con Windows (checkable) · Salir.
  Double-click → Abrir.
- Start with Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `CamaraWin` =
  `"<exe path>" --tray`. Toggle reflects the registry. Default off. `--tray` → start hidden.
- Single instance: a second launch activates the running instance (named mutex + named event the
  first instance waits on) and exits.
- Errors while hidden: only recording sessions are connected; their notices use NotifyIcon
  balloon tips instead of in-window toasts.

## 6. Grabar todas

- Toolbar button "⏺ Grabar todas" / "⏹ Detener todas" (label reflects whether any camera is
  recording). Grabar todas starts recording on every visible camera not already recording;
  Detener todas stops all recordings (tiles and fullscreen). Same per-camera logic as the tile
  button (mainstream headless session). Also in the tray menu (there "visible" = all cameras).

## 7. Stats overlay

- `StreamSession.Stats` → `StreamStats(double Fps, double LatencyMs, bool HardwareDecoding,
  TimeSpan SinceLastFrame)`, produced on the session thread, read lock-free (immutable record
  swapped via `Volatile.Write`).
  - Fps: frames presented in the last 1 s window.
  - LatencyMs: exponential moving average of (frame published − its packet returned by
    `av_read_frame`), in ms.
  - HardwareDecoding: decoded frame format was D3D11.
- Tile overlay (bottom-left, small text) when `ShowStats` is on: `25 fps · 38 ms · GPU`,
  refreshed twice per second; orange when `SinceLastFrame` > 1 s.

## 8. Testing
- Core: `GridLayout.ComputeFeatured` (n = 0,1,2,3,7,8,9,10), `ErrorTranslator` (every kind × brand
  returns non-empty Spanish text; Short ≤ 30 chars), settings round-trip of LayoutMode/FeaturedCameraId.
- Media: `StreamErrorClassifier` table tests; RTSP status-line parser tests with real FFmpeg
  log strings; credential sanitizer tests; integration (mediamtx): wrong password → AuthFailed +
  ErrorOccurred with RtspStatus 401; unknown path → NotFound; unreachable port → Unreachable;
  Detail never contains the password.
- Core: `CameraBackup` round-trip with and without passphrase; wrong passphrase →
  `BackupPassphraseException`; corrupt/foreign JSON and version 2 → `BackupFormatException`;
  swapped entry fails authentication; export never contains a plaintext password;
  `docs/ejemplo-camaras.json` imports cleanly; merge function (host+port match); auto-start
  registry helper behind an interface (tested with a fake).
- Media: Stats fps/latency/hardware flag populated during the mediamtx integration run.
- App: build + smoke run (menu opens, export/import round-trip in a temp folder, tray hide/show,
  second instance activates the first); manual check with the user's cameras (wrong password on
  one Tapo and one Imou must show "Contraseña incorrecta" and a toast).
