# Centinela v1.4 — Fase 2d: bandeja, avisos, movimiento, registros y actualizaciones — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El Centinela Avalonia alcanza la paridad con la WPF en Windows: icono de la bandeja con su menú y sus avisos del sistema, la X que oculta en la bandeja, arranque oculto (`--tray`) y «Arrancar con Windows», avisos dentro de la ventana, registro de errores, sonidos de aviso, detección de movimiento (marco rojo, avisos, registro), actualizaciones, y la lista de paridad para que el usuario la recorra antes de borrar la WPF.

**Architecture:** Lo de sistema pasa por interfaces de `Centinela.Core.Platform` con su implementación en `Centinela.Platform`: la bandeja de Windows (`ITray` → `WindowsTray`, icono propio con `Shell_NotifyIcon` y sus globos, que Windows 10/11 muestra como notificaciones del sistema, igual que la 1.3). La lógica de avisos, errores, movimiento y actualizaciones vive en servicios de `Centinela.Desktop/Services/<área>/` (puertos de los de la WPF, con `UiThread.Post` en lugar del `Dispatcher` de WPF); las vistas solo pintan. `MainWindow` crece solo con archivos parciales por tema (`MainWindow.Tray.cs`, `MainWindow.Errors.cs`, `MainWindow.Motion.cs`, `MainWindow.Updates.cs`).

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22, CommunityToolkit.Mvvm 8.4.2, Silk.NET.OpenAL 2.23.0, P/Invoke de Win32 (`shell32`/`user32`) en `Centinela.Platform/Windows`, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (§2 plataforma, §3 interfaz, §5 actualizaciones). Pendientes: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md` (secciones «Fase 2», «2d», «Arrastrados de la fase 2c › 2d»).

**Decisión del usuario (1 de octubre):** en Windows la bandeja y sus avisos los hace Centinela con la API de Win32, como la 1.3 (los globos salen como notificaciones de Windows y al pulsarlos abren la ventana), en lugar del `TrayIcon` de Avalonia más notificaciones WinRT que proponía el diseño. En macOS y Linux (fases 3–4) se implementará la misma `ITray` con el `TrayIcon` de Avalonia y las notificaciones de cada sistema.

**Decisión (sonidos):** los dos sonidos de aviso propios que pide el diseño se sintetizan en código (tonos cortos) en lugar de incluir archivos `.wav`; suenan igual en los tres sistemas por OpenAL.

**Referencia de comportamiento:** la WPF, que no se toca: `src/Centinela.App/TrayController.cs`, `TrayIcons.cs`, `MainWindow.Tray.cs`, `App.xaml.cs` (`--tray`, excepciones no controladas), `Toast.cs`, `ToastHost.xaml(.cs)`, `ErrorCenter.cs`, `MainWindow.Errors.cs`, `ErrorLogWindow.xaml(.cs)`, `MainWindow.Streams.cs` (`WireSharedErrors`), `MotionService.cs`, `MainWindow.Motion.cs`, `MotionLogWindow.xaml(.cs)`, `CameraTile.xaml.cs` (`SetMotionActive`, `UpdateBorder`), `MainWindow.Updates.cs`, `UpdateWindow.xaml(.cs)`, `MainWindow.xaml` (barra de estado con «Movimiento» y «Registro»).

**Fuera de esta fase:** bandeja, avisos y autoarranque de macOS y Linux (fases 3–4); borrar `src/Centinela.App` (se hace tras el visto bueno del usuario a la lista de paridad, en un paso aparte).

## Global Constraints

- .NET 10; Avalonia 11.3.22; CommunityToolkit.Mvvm 8.4.2; Silk.NET.OpenAL 2.23.0. Sin paquetes NuGet nuevos.
- `src/Centinela.App` (WPF) **no se modifica** y debe seguir compilando.
- Textos visibles idénticos a los de la WPF (menús, globos, avisos, ventanas, botones, tooltips), salvo donde este plan diga otra cosa.
- Un archivo, una responsabilidad; ningún archivo de más de ~300 líneas.
- Las vistas sin lógica de negocio. `Centinela.Desktop` no usa APIs de Windows: todo lo de Win32 vive en `Centinela.Platform/Windows`.
- Contraseñas nunca en texto claro en registros, avisos ni mensajes (los detalles de error pasan por `CredentialSanitizer` donde ya lo hacen en la WPF).
- La comprobación de actualizaciones no envía datos del usuario (solo la petición a la API de GitHub) y nunca descarga ni instala nada.
- Con `CENTINELA_DATA_DIR`: nada real del sistema (sin autoarranque real, sin comprobación de actualizaciones al arrancar).
- Pruebas solo de código (lógica pura, carpetas temporales). Ninguna prueba toca `%APPDATA%\Centinela`, Documentos, Vídeos, Imágenes, el registro (clave Run), la bandeja real ni la red.
- Compilar Desktop/App/solución y ejecutar las pruebas de Desktop siempre con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/`. No ejecutar la aplicación ni tocar ningún Centinela en marcha.
- `git add` con rutas explícitas (nunca los `README.sync-conflict-*.md`); commits con línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Cero advertencias de compilación.

---

### Task 1: Pendientes pequeños

**Files:**
- Modify: `src/Centinela.Desktop/Program.cs`, `src/Centinela.Desktop/App.axaml.cs`
- Modify: `src/Centinela.Platform/Windows/WindowsSingleInstance.cs`, `tests/Centinela.Platform.Tests/` (la prueba de instancia única que ya exista, o una nueva `SingleInstanceTests.cs`)
- Modify: `src/Centinela.Desktop/Services/Audio/AudioController.cs`, `src/Centinela.Desktop/Styles/TileButtons.axaml`, `src/Centinela.Desktop/Views/CameraTileView.Actions.cs`, `src/Centinela.Desktop/ViewModels/MainViewModel.Recording.cs` (y sus usos)

**Interfaces:** sin cambios públicos salvo el renombrado del VM indicado abajo.

- [ ] **Excepciones no controladas** (como `DispatcherUnhandledException` de la WPF): en `App` (antes de crear la ventana) `Dispatcher.UIThread.UnhandledException += (_, e) => { e.Handled = true; _ = MessageDialog.ShowAsync(MainWindow visible o null, e.Exception.Message); }`. `TaskScheduler.UnobservedTaskException` → `e.SetObserved()` (sin mensaje).
- [ ] **`Program.Main`**: `TryClaim()` dentro del `try` (si lanza, `App.StartupError = ex.Message`).
- [ ] **`WindowsSingleInstance`**: `TryClaim` y `ListenForActivation` repetidos devuelven/conservan el primer resultado (no crean otra tubería); `<inheritdoc/>` en `TryClaim`; el comentario del `ObjectDisposedException` sin dramatismo. Prueba: dos `TryClaim()` seguidos sobre la misma instancia devuelven `true` las dos veces (con un sufijo único, como las pruebas existentes).
- [ ] **`AudioController.Toggle`**: salir sin hacer nada si el mosaico está cerrado (desechado) o no puede sonar (`!AudioCapable`).
- [ ] **Estilo**: quitar el encogido de Fluent al pulsar un `Button.tile` (la WPF no encoge): en `TileButtons.axaml`, `Style Selector="Button.tile:pressed"` con `RenderTransform` vacío (`none`).
- [ ] **Menú del mosaico**: el sincronizador de marcas del menú contextual (`CameraTileView.Actions.cs`) solo reacciona a los cambios de las propiedades que pinta (filtrar `PropertyChanged` por nombre), no a todas.
- [ ] **VM**: renombrar `MainViewModel.AnyRecording` (si existe con ese nombre en el VM) a `IsAnyRecording`, para no confundirlo con `RecordingController.AnyRecording`.
- [ ] Pruebas de Platform y Desktop pasan; solución con 0 advertencias. **Commit** — `fix(desktop): pendientes pequeños de la fase 2d`.

---

### Task 2: Bandeja de Windows (`ITray` + `WindowsTray`)

**Files:**
- Create: `src/Centinela.Core/Platform/ITray.cs`
- Create: `src/Centinela.Platform/Windows/WindowsTray.cs`, `src/Centinela.Platform/Windows/TrayInterop.cs` (P/Invoke y estructuras), `src/Centinela.Platform/Windows/TrayText.cs` (puro)
- Modify: `src/Centinela.Platform/PlatformServices.cs` (`Func<ITray>? CreateTray`)
- Create: `tests/Centinela.Platform.Tests/TrayTextTests.cs`

**Interfaces:**
- Produces (`Centinela.Core.Platform`):

```csharp
namespace Centinela.Core.Platform;

/// <summary>A 32-bit image for the tray icon: straight (not premultiplied) BGRA, rows top to bottom.</summary>
public sealed record TrayImage(int Width, int Height, byte[] Bgra);

/// <summary>One entry of the tray menu; a null <see cref="Text"/> is a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? OnClick = null, bool Checkable = false, bool Checked = false);

/// <summary>
/// The icon in the system tray (Windows: notification area), its menu and its notices. Create, use and dispose on
/// the UI thread. Events are raised on the UI thread.
/// </summary>
public interface ITray : IDisposable
{
    void SetIcon(TrayImage image, string tooltip);
    /// <summary>Replaces the whole menu (it is rebuilt each time it opens from the latest items).</summary>
    void SetMenu(IReadOnlyList<TrayMenuItem> items);
    /// <summary>A system notice; a click on it runs <paramref name="onClick"/>, or raises <see cref="OpenRequested"/> without one.</summary>
    void ShowNotice(string title, string text, Action? onClick = null);
    /// <summary>Double click on the icon.</summary>
    event Action? OpenRequested;
    /// <summary>The menu is about to show (refresh state that may have changed outside the app).</summary>
    event Action? MenuOpening;
}
```

- `PlatformServices.CreateTray` (`Func<ITray>?`): en Windows `() => new WindowsTray()`; en el resto `null` (fases 3–4). También con `CENTINELA_DATA_DIR` (la bandeja no toca datos).
- `static class TrayText { const int TipMax = 127, TitleMax = 63, InfoMax = 255; static string Fit(string text, int max); }` — recorta al máximo de `NOTIFYICONDATAW` (con «…» al final si se recorta) y nunca devuelve vacío para el texto del globo (" " como la WPF).

`WindowsTray` (puerto de `TrayController` sin WinForms):
- Una ventana solo de mensajes (`CreateWindowExW` con `HWND_MESSAGE` y una clase registrada con su `WndProc` guardado en un campo para que no lo recoja el GC); se crea en el hilo de la interfaz, cuyo bucle de mensajes (el de Avalonia) la atiende.
- `Shell_NotifyIconW` `NIM_ADD`/`NIM_MODIFY`/`NIM_DELETE` con `NOTIFYICON_VERSION_4` (`NIM_SETVERSION`) y `uCallbackMessage` propio. El icono: `CreateIconIndirect` con un mapa de bits de 32 bits (DIB de `CreateDIBSection`) a partir de `TrayImage`, y la máscara; se libera el anterior con `DestroyIcon`.
- Mensajes: `WM_CONTEXTMENU` (o clic derecho) → `MenuOpening`, construir el menú con `CreatePopupMenu`/`AppendMenuW` (`MF_SEPARATOR`, `MF_CHECKED`), `SetForegroundWindow` + `TrackPopupMenuEx` con `TPM_RETURNCMD`, ejecutar el `OnClick` elegido y `PostMessage(WM_NULL)`; `WM_LBUTTONDBLCLK` → `OpenRequested`; `NIN_BALLOONUSERCLICK` → la acción del último globo (cada `ShowNotice` la sustituye) o `OpenRequested`.
- Globo: `NIF_INFO` con `NIIF_INFO`, título y texto con `TrayText.Fit`.
- Si el explorador se reinicia (mensaje registrado `TaskbarCreated`), volver a añadir el icono.
- `Dispose`: `NIM_DELETE`, destruir menú, icono y ventana; idempotente; nunca lanza.

- [ ] **Step 1: Pruebas** de `TrayText`: texto corto sin cambios; uno de 300 caracteres con `InfoMax` → 255 caracteres terminados en «…»; vacío con `InfoMax` → " ".
- [ ] **Step 2:** ejecutarlas (fallan). **Step 3:** implementar `ITray`, `TrayText`, `TrayInterop`, `WindowsTray` y `CreateTray`. Marcar `WindowsTray` con `[SupportedOSPlatform("windows")]`. Ninguna prueba crea un `WindowsTray` (tocaría la bandeja real).
- [ ] **Step 4:** pruebas de Platform pasan; solución con 0 advertencias. **Step 5: Commit** — `feat(platform): bandeja de Windows con menú y avisos`.

---

### Task 3: Modo bandeja en la ventana principal

**Files:**
- Create: `src/Centinela.Desktop/Services/Tray/TrayIconRenderer.cs` (dibuja el icono normal y el de grabación con Avalonia)
- Create: `src/Centinela.Desktop/Services/Tray/TrayMenu.cs` (puro: los elementos del menú y sus textos)
- Create: `src/Centinela.Desktop/Services/Tray/TrayTexts.cs` (puro: textos según el sistema)
- Create: `src/Centinela.Desktop/Views/MainWindow.Tray.cs`
- Modify: `src/Centinela.Desktop/App.axaml.cs`, `src/Centinela.Desktop/Program.cs` (`--tray`, cierre explícito), `src/Centinela.Desktop/Views/MainWindow.axaml.cs` (`OnClosing`/`OnClosed`), `src/Centinela.Desktop/Views/MainWindow.Recording.cs` («Grabar todas» de la bandeja)
- Create: `tests/Centinela.Desktop.Tests/TrayMenuTests.cs`, `tests/Centinela.Desktop.Tests/TrayIconRendererTests.cs`

**Interfaces:**
- Consumes: `ITray`, `TrayImage`, `TrayMenuItem`, `PlatformServices.CreateTray`, `IAutoStart` (`App.Platform.AutoStart`, null con `CENTINELA_DATA_DIR`), `RecordAll.Targets`.
- Produces:
  - `static class TrayTexts { static string AutoStartLabel(OsKind os); }` — Windows «Arrancar con Windows», macOS «Abrir al iniciar sesión», Linux «Arrancar al iniciar sesión». Y `static string AutoStartUnavailable(OsKind os)` = «{AutoStartLabel} no está disponible con CENTINELA_DATA_DIR.» (pendiente de la fase 2: textos de la bandeja según el sistema).
  - `static class TrayMenu { static IReadOnlyList<TrayMenuItem> Build(bool anyRecording, bool autoStartAvailable, bool autoStartEnabled, OsKind os, Action open, Action recordAll, Action<bool> setAutoStart, Action exit); }` — «Abrir», «Grabar todas» / «Detener todas», (si está disponible) la casilla de autoarranque, separador, «Salir».
  - `static class TrayIconRenderer { static TrayImage Render(bool recording); static void Unpremultiply(Span<byte> bgra); }` — el logo (`/Assets/centinela.png`) a 32×32 y, si graba, un punto rojo con aro blanco abajo a la derecha (diámetro 7/16 del lado, como `TrayIcons.cs`); `Unpremultiply` es puro.

Comportamiento (puerto de `MainWindow.Tray.cs` y `App.xaml.cs`):
- El cierre de la aplicación pasa a ser explícito (`ShutdownMode.OnExplicitShutdown`): solo «Salir» (menú Archivo o bandeja), el cierre de sesión del sistema (`IClassicDesktopStyleApplicationLifetime.ShutdownRequested` → salida real) o no tener bandeja terminan la app.
- **X de la ventana**: con bandeja (`CreateTray` no nulo y creado sin error) y sin salida pedida → cancela el cierre y oculta en la bandeja; sin bandeja → cierra (sale). La posición de la ventana se guarda **solo si el cierre sigue adelante** (pendiente 2a); al ocultar se guarda como en la WPF (`SaveWindowPlacement` en `HideToTray`).
- **Ocultar** (`HideToTray`): cierra las pantallas completas, retira todos los mosaicos (las grabaciones y, en la Task 6, la detección siguen), suelta el sonido, `Hide()`; la primera vez, globo «Centinela sigue en la bandeja» / «Las grabaciones continúan. Doble clic en el icono para abrirla; «Salir» la cierra.» y guarda `TrayHintShown`.
- **Mostrar** (`ShowFromTray`, también con la activación de otra instancia): si estaba oculta, `Show()` y reconstruir la vista; restaurar si estaba minimizada; `Activate()`. Nada si se está cerrando o saliendo.
- **`--tray`** (autoarranque): la ventana se crea pero no se muestra (cuenta como oculta en la bandeja); sin bandeja, se muestra.
- **Menú de la bandeja**: «Abrir»; «Grabar todas» / «Detener todas» (todas las cámaras, también con la ventana abierta: `RecordAll.Targets(..., allCameras: true)`; con la ventana oculta, la barra también graba todas); casilla de autoarranque (solo si `AutoStart` no es nulo; consulta el estado real al abrir el menú; al cambiarla `Enable`/`Disable`, y si falla: «No se pudo cambiar el arranque con Windows: {mensaje}» —con la etiqueta del sistema— y se vuelve a leer); separador; «Salir». Al arrancar, `AutoStart.Migrate()` en silencio.
- **Icono**: normal o con punto rojo según `RecordingController.AnyRecording` (en cada `StatusChanged`), tooltip «Centinela».
- **Fallos con la ventana oculta**: los mensajes de `RecordingController.Failure` van a la línea de estado y, si está oculta, también a un globo «Centinela».
- **Guardas de cierre** (pendiente 2b): `_closing` en `OnClosing` cuando el cierre sigue; nada toca la ventana ni el VM tras cerrar; crear la carpeta de grabaciones fuera de la vista (en el controlador o el VM).
- **Salir**: `OnClosed` hace lo de hoy (espera acotada de mosaicos, grabaciones, copias…), además `_tray.Dispose()`, y luego `desktop.Shutdown()`.

- [ ] **Step 1: Pruebas**: `TrayMenu.Build` (textos y orden con y sin grabación; sin autoarranque no hay casilla; la casilla lleva `Checkable` y su estado; cada elemento llama a su acción), `TrayTexts` de los tres sistemas, `TrayIconRenderer.Unpremultiply` (píxel con alfa 128 y canal 64 → 127/128; alfa 0 → todo 0; alfa 255 sin cambios). `Render` no se prueba (necesita la plataforma de Avalonia).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas de Desktop pasan; solución con 0 advertencias. **Step 5: Commit** — `feat(desktop): modo bandeja, arranque oculto y autoarranque`.

---

### Task 4: Avisos dentro de la ventana y sonidos de aviso

**Files:**
- Create: `src/Centinela.Desktop/Services/Notices/Toast.cs` (`enum ToastStyle { Error, Recovery, Info }`, `record Toast(string Title, string Message, ToastStyle Style, string? ActionText = null, Action? OnAction = null)`)
- Create: `src/Centinela.Desktop/Views/ToastHost.axaml(.cs)`
- Create: `src/Centinela.Desktop/Services/Notices/NoticeRouter.cs`
- Create: `src/Centinela.Desktop/Services/Audio/NoticeTones.cs` (puro: síntesis), `src/Centinela.Desktop/Services/Audio/NoticeSoundPlayer.cs`
- Modify: `src/Centinela.Desktop/Services/Audio/OpenAlAudioOutput.cs` (exponer su candado `Gate` como `internal` para compartirlo)
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml(.cs)` (el `ToastHost` sobre la rejilla)
- Create: `tests/Centinela.Desktop.Tests/NoticeTonesTests.cs`, `tests/Centinela.Desktop.Tests/NoticeRouterTests.cs`

**Interfaces:**
- Produces:
  - `ToastHost`: `void Show(Toast toast)`, `event Action? OpenLogRequested` — puerto de `ToastHost.xaml.cs`: abajo a la derecha, como mucho 4 (se quitan los más antiguos), duración 8 s error / 4 s recuperación / 12 s información, colores `#5A1F1F` / `#1E4D2B` / `#1F3A5A`, título seminegrita blanco, mensaje `#DDDDDD`, texto de acción subrayado `#9CD0FF`, botón «✕»; clic → su acción o abrir el registro.
  - `sealed class NoticeRouter(Func<bool> hiddenInTray, Action<Toast> showInWindow, Action<string, string, Action?> showSystemNotice)` con `void Show(Toast toast)` — con la ventana oculta en la bandeja, aviso del sistema (`ITray.ShowNotice(title, message, toast.OnAction)`); si no, dentro de la ventana. Puro (lo prueban delegados falsos).
  - `static class NoticeTones { const int SampleRate = 48000; static float[] ConnectionLost(); static float[] Motion(); }` — mono float: conexión perdida = dos tonos descendentes (880 Hz y 660 Hz, 150 ms cada uno); movimiento = dos pitidos cortos de 1000 Hz (80 ms, 60 ms de silencio entre ellos); envolvente de 5 ms de subida y bajada; amplitud máxima 0,5.
  - `sealed class NoticeSoundPlayer : IDisposable { void Play(NoticeKind kind); }` — abre su propio dispositivo/contexto OpenAL al primer uso bajo el mismo candado `OpenAlAudioOutput.Gate` (haciendo actual su contexto en cada operación), carga los dos búferes (PCM16 mono a 48 kHz con `SampleConversion`), reproduce sin bloquear; si OpenAL no está disponible, no suena y no falla.

- [ ] **Step 1: Pruebas**: `NoticeTones` (duraciones exactas en muestras: 2×150 ms = 14 400; 80+60+80 ms = 10 560; máximo absoluto ≤ 0,5; primera y última muestra 0); `NoticeRouter` (oculta → aviso del sistema con título, mensaje y la acción; visible → dentro de la ventana).
- [ ] **Step 2:** fallan. **Step 3:** implementar (el `ToastHost` va encima de la rejilla, abajo a la derecha, como en la WPF). **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(desktop): avisos en la ventana y sonidos de aviso`.

---

### Task 5: Centro de errores y «Registro de errores»

**Files:**
- Create: `src/Centinela.Desktop/Services/Errors/ErrorCenter.cs` (puerto de `src/Centinela.App/ErrorCenter.cs`, usando `ErrorText.Translate` y `Toast` de la Task 4)
- Create: `src/Centinela.Desktop/Views/ErrorLogWindow.axaml(.cs)`
- Create: `src/Centinela.Desktop/Views/MainWindow.Errors.cs`
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml` (barra de estado: botones a la derecha), `src/Centinela.Desktop/Views/MainWindow.Cameras.cs`, `src/Centinela.Desktop/Views/MainWindow.Backup.cs`, `src/Centinela.Desktop/Views/MainWindow.Audio.cs`, `src/Centinela.Desktop/Services/Streams/StreamLeases.cs`, `src/Centinela.Desktop/ViewModels/TileViewModel.cs`
- Create: `tests/Centinela.Desktop.Tests/ErrorCenterTests.cs`

**Interfaces:**
- Consumes (Core): `ErrorLog` (`Add`, `Snapshot`, `Clear`, `EntryAdded`, `FormatLine`, `PurgeOlderThan`), `ErrorLogEntry`, `ErrorNotificationPolicy`, `NotificationGate.Decide`, `NoticeKind`, `NoticeDecision`.
- Produces: `sealed class ErrorCenter(ErrorLog log, Func<NoticeKind, Camera, NoticeDecision> decide)` con `Report(Camera, StreamError)`, `Playing(Camera)`, `Forget(Guid)`, `MarkRead()`, `int Unread`, eventos `ToastRequested`, `SoundRequested`, `UnreadChanged` — mismos textos y reglas que la WPF.

Conexiones (puerto de `MainWindow.Errors.cs` y `WireSharedErrors`):
- Al arrancar: purgar registros antiguos (`ErrorLog.PurgeOlderThan(AppPaths.LogsDirectory, DateTime.Now)`, en silencio) y crear el registro.
- `decide` = `NotificationGate.Decide(kind, cámara actual, settings, ventanaVisible, DateTime.Now)` con ventanaVisible = mostrada, no oculta en la bandeja y no minimizada.
- `ToastRequested` → `NoticeRouter.Show`; `SoundRequested(ConnectionLost)` → `NoticeSoundPlayer.Play`; `UnreadChanged` → botón «Registro» / «Registro ({n})».
- Mosaicos con sesión propia: `ErrorReported` → `Report`, llegada a `Playing` → `Playing` (añadir a `TileViewModel` el evento `PlayingReached` si no existe, solo en sesiones propias). Sesiones compartidas: `StreamLeases.SessionCreated` conecta una vez por sesión `ErrorOccurred` y `StateChanged`(`Playing`) publicando en el hilo de interfaz (`UiThread.Post`, **nunca** dentro del callback de creación: pendiente de reentrada) y buscando la cámara actual por Id (y misma URL y UDP, como `CurrentCameraFor`). `RecordingController.ErrorOccurred` / `SessionStateChanged` también.
- **Editar en sesiones compartidas** que arrancan con error de contraseña: el mosaico muestra el botón «Editar» igual que con sesión propia (pendiente 2b).
- **Fallo de audio**: además del aviso actual, `ErrorLog.Add` con cámara, «Audio», el texto y el detalle.
- `errors.Forget(id)` al añadir, editar, eliminar e importar (las cámaras actualizadas).
- Ventana «Registro de errores» (900×480): sin `DataGrid` (cabecera + `ListBox` de filas `Grid` con selección múltiple; columnas Hora `dd/MM HH:mm:ss`, Cámara, Error, Título, Detalle), la más nueva arriba y siguiendo las nuevas mientras está abierta (`EntryAdded` llega en cualquier hilo: `UiThread.Post`); «Copiar» (seleccionadas o todas, con `ErrorLog.FormatLine`, portapapeles de Avalonia), «Abrir carpeta» (`App.Platform.Files.OpenFolder` tras crearla), «Limpiar»; tooltips de la WPF. Abrirla pone a cero los no leídos; si ya está abierta, se activa.
- Barra de estado: a la derecha «Movimiento» (lo conecta la Task 6; aquí ya el botón con su tooltip «Registro de movimiento») y «Registro» (tooltip «Registro de errores»).

- [ ] **Step 1: Pruebas** de `ErrorCenter` con un `ErrorLog` en carpeta temporal y un `decide` falso: un error se registra y cuenta como no leído; dos iguales seguidos avisan una sola vez (política); con `decide` que no muestra no hay aviso pero sí registro; `Playing` tras un error registra «Recuperada» y avisa «✓ {nombre}: conexión recuperada»; `MarkRead` pone 0; `Translate` con contraseña vacía y `AuthFailed` da «Falta contraseña».
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(desktop): centro de errores y registro de errores`.

---

### Task 6: Detección de movimiento

**Files:**
- Create: `src/Centinela.Desktop/Services/Motion/MotionService.cs` (puerto de `src/Centinela.App/MotionService.cs`: `UiThread.Post` en lugar del `Dispatcher`; si pasa de ~300 líneas, repartir el monitor por cámara en `CameraMonitor.cs`)
- Create: `src/Centinela.Desktop/Views/MotionLogWindow.axaml(.cs)`
- Create: `src/Centinela.Desktop/Views/MainWindow.Motion.cs`
- Modify: `src/Centinela.Desktop/ViewModels/TileViewModel.Display.cs` (`MotionActive`), `src/Centinela.Desktop/Views/CameraTileView.axaml(.cs)` (marco rojo), `src/Centinela.Desktop/Services/Cameras/CameraActions.cs`, `src/Centinela.Desktop/Views/MainWindow.Cameras.cs`, `MainWindow.Backup.cs`, `MainWindow.Options.cs`, `MainWindow.axaml.cs` (espera al cerrar)
- Create: `tests/Centinela.Desktop.Tests/MotionServiceTests.cs`

**Interfaces:**
- Consumes (Core/Media): `IMotionDetector`, `FrameDiffDetector`, `GrayScaler`, `MotionTracker`, `MotionEvent`, `MotionLog`, `MotionLogEntry`, `NotificationGate`, `SharedStreamLease`.
- Produces: `MotionService(Func<Camera, SharedStreamLease?> acquire, Func<MotionSensitivity, IMotionDetector>? detectorFactory = null)` con la API de la WPF (`Apply`, `IsActive`, `ConfirmAlert`, `ShutdownAsync`, `Dispose`, eventos `MotionChanged`, `MotionStarted`, `MotionEnded`, `DetectorFailed`); `TileViewModel.MotionActive` (+ `SetMotionActive(bool)`).

Comportamiento (puerto de `MainWindow.Motion.cs` y `CameraTile.UpdateBorder`):
- Se crea en el constructor (también arrancando oculta); `acquire` = `StreamLeases.AcquireSub(camera, 320, 180)`. Purga y crea el `MotionLog`.
- Marco: con movimiento, borde de 3 px `#E53935` en cada vista de la cámara (rejilla, tapas y pantallas completas; también las creadas después); al arrastrar encima, el azul de soltar (con 3 px si además hay movimiento); si no, el normal de 1 px.
- `MotionStarted` con aviso posible → `NotificationGate.Decide(Motion, …)`: si se muestra, aviso del sistema «Detección de movimiento: «{nombre}»» / hora `HH:mm:ss` que al pulsarlo abre la ventana (`ShowFromTray`), y `ConfirmAlert`; si toca sonido, `NoticeSoundPlayer.Play(Motion)`. Solo con la ventana minimizada u oculta (lo decide `NotificationGate` con ventanaVisible).
- `MotionEnded` → `MotionLog.Add` y «Movimiento ({n})». `DetectorFailed` → `ErrorLog.Add` («Movimiento», «{nombre}: la detección de movimiento se ha desactivado», mensaje).
- `motion.Apply(cámaras)` al arrancar y tras añadir, editar, eliminar, importar, «Opciones de avisos» y el 👁 (`CameraActions.ToggleMotion`).
- Ventana «Registro de movimiento» (640×480): columnas Hora, Cámara, Duración (`MotionLog.FormatDuration`), Cambio máx. (`0.0 %`); «Copiar», «Abrir carpeta», «Vaciar» con los tooltips de la WPF; sigue las nuevas; abrirla pone a cero el contador.
- Al cerrar: `motion.ShutdownAsync()` en la espera acotada (antes de cerrar el registro), como la WPF.

- [ ] **Step 1: Pruebas** de `MotionService` con un `acquire` que devuelve null y un detector falso donde haga falta: `Apply` solo vigila cámaras con `MotionEnabled` y URL secundaria; `Apply` sin la cámara la deja de vigilar; `IsActive` es falso sin eventos; `ShutdownAsync` termina y después `Apply` no hace nada. (Si la clase necesita una sesión real para algo más, probar solo lo que no la necesite.)
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(desktop): detección de movimiento con marco, avisos y registro`.

---

### Task 7: Actualizaciones

**Files:**
- Create: `src/Centinela.Core/Updates/UpdateAssets.cs`
- Create: `src/Centinela.Desktop/Views/UpdateWindow.axaml(.cs)`
- Create: `src/Centinela.Desktop/Views/MainWindow.Updates.cs`
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml` (Ayuda › «_Buscar actualizaciones…» antes de «Acerca de», tras el separador)
- Create: `tests/Centinela.Core.Tests/UpdateAssetsTests.cs`

**Interfaces:**
- Produces: `static class UpdateAssets { static string FileName(Version version, OsKind os, Architecture arch); }` — diseño §4/§5: `Centinela-{v}-windows-x64.exe`, `Centinela-{v}-macos-arm64.dmg`, `Centinela-{v}-macos-intel.dmg` (x64), `centinela_{v}_amd64.deb` (Linux x64); `{v}` = mayor.menor.parche.

Comportamiento (puerto de `MainWindow.Updates.cs` y `UpdateWindow.xaml(.cs)`, mismos textos):
- Comprobación automática 10 s después de arrancar si `UpdatePolicy.ShouldAutoCheck` y sin `CENTINELA_DATA_DIR`; vuelve a mirar `CheckUpdatesOnStartup` antes; guarda `LastUpdateCheck` si cuenta; un fallo va al registro de errores («Centinela», «Actualizaciones», «No se pudo buscar actualizaciones», detalle); si toca avisar: con la ventana visible, aviso «Centinela {versión} disponible» / título de la versión / acción «Ver» (abre la ventana de actualizaciones); oculta en la bandeja, aviso del sistema «Centinela {versión} disponible» / «Haz clic aquí para ver la novedad.» que abre la ventana y los detalles; y si se abre la ventana de otra forma, el aviso normal una vez (`_pendingUpdate`).
- Ventana «Actualizaciones» (520×420, mínimo 420×260): «Versión instalada: {AppInfo.Version}», estado en seminegrita, notas (solo si hay versión nueva), **línea nueva**: «Archivo para este equipo: {UpdateAssets.FileName(...)}» bajo el estado cuando hay versión nueva; «Comprobar al arrancar»; «Descargar» (abre la página de la versión con `Links.OpenAsync`, nunca descarga), «Omitir esta versión», «Volver a comprobar», «Cerrar». Un solo `HttpClient` estático para el `UpdateChecker`. Si ya está abierta, se activa.

- [ ] **Step 1: Pruebas** de `UpdateAssets` (los cuatro nombres; `1.4` se escribe `1.4.0`; un sistema/arquitectura sin paquete —p. ej. Linux arm64— lanza `PlatformNotSupportedException`, y la ventana entonces no muestra la línea).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(desktop): buscar actualizaciones`.

---

### Task 8: Lista de paridad y comprobación de la fase 2d

**Files:**
- Create: `docs/paridad-v1.4.md`
- Modify: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md`

- [ ] `docs/paridad-v1.4.md`: lista con casillas (`- [ ]`) de **todas** las funciones que describe el `README.md` y el Manual (`ManualDocument`) agrupadas como el Manual (añadir cámaras, buscar en red, vistas, clic derecho, zoom, sonido, grabar y capturas, bandeja, arrancar con Windows, detección de movimiento, registro de movimiento, opciones de avisos, copias de seguridad, actualizaciones, errores y registro, estadísticas, calidad de la cámara, suavizar, HD/SD), cada una con lo que hay que hacer y lo que debe verse. Encabezado: «Recórrela en Windows con el Centinela nuevo (cerrando antes el de siempre). Cuando todo esté marcado, se borra la versión WPF.»
- [ ] Pruebas de Core, Platform, Media y Desktop pasan; solución con 0 advertencias; nada de APIs de Windows en `src/Centinela.Desktop` (`grep` de `System.Windows`, `Microsoft.Win32`, `Registry`, `DllImport`, `LibraryImport`); ningún archivo nuevo pasa de ~300 líneas.
- [ ] Pendientes: marcar como hechos los de «2d» resueltos aquí y añadir lo que quede abierto.
- [ ] **Commit** — `docs: lista de paridad de la v1.4`.
