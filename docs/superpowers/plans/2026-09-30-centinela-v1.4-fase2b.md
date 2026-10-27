# Centinela v1.4 — Fase 2b: botones y menú de cada cámara — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El Centinela Avalonia gana todo lo de cada cámara que tiene la WPF: barra de botones (HD/SD, 🔇/🔊, 👁, 📷, ⏺, ✎, 🗑) con el estilo actual, menú del clic derecho, zoom digital, sonido (OpenAL, multiplataforma), capturas, grabación (una y todas), «Suavizar imagen», «Añadir / Editar / Duplicar / Eliminar» con su diálogo y «Probar», más los pendientes de la 2a.

**Architecture:** Igual que la 2a: la vista (`CameraTileView`) pinta y recoge clics y los pasa como eventos; `TileViewModel` guarda el estado que se ve (repartido en archivos parciales por tema); cada acción vive en su servicio (`RecordingController`, `SnapshotService`, `AudioController`, `CameraActions`). Lo de sistema (abrir carpetas) pasa por `PlatformServices`.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22, CommunityToolkit.Mvvm 8.4.2, Silk.NET.OpenAL 2.23.0 + Silk.NET.OpenAL.Soft.Native 1.23.1, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (§2 audio OpenAL, §3 interfaz). Pendientes: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md` (sección «2b»).

**Referencia de comportamiento:** la WPF, que no se toca: `src/Centinela.App/CameraTile.xaml(.cs)`, `App.xaml` (estilos de botón), `MainWindow.View.cs`, `MainWindow.xaml.cs` (Add/Edit/Duplicate/Delete, grabaciones, estado), `MainWindow.Audio.cs`, `AudioOutput.cs`, `RecordingController.cs`, `AddCameraDialog.xaml(.cs)`, `MainWindow.Motion.cs` (`ToggleMotion`).

**Decisión (dónde vive la salida de audio):** el diseño la pone en `Centinela.Platform`. Como OpenAL ya es multiplataforma y la salida implementa `IAudioSink` (de Media, que Platform no referencia), va en `Centinela.Desktop/Services/Audio`. Nada cambia para el usuario.

**Fuera de esta fase:** detección de movimiento (el 👁 solo guarda la opción y muestra el indicador; la detección, el marco rojo y los avisos llegan en 2d), «Calidad de la cámara…» y demás diálogos (2c), bandeja y avisos (2d).

## Global Constraints

- .NET 10; Avalonia 11.3.22; CommunityToolkit.Mvvm 8.4.2; Silk.NET.OpenAL 2.23.0 y Silk.NET.OpenAL.Soft.Native 1.23.1.
- `src/Centinela.App` (WPF) **no se modifica**.
- Textos visibles idénticos a los de la WPF (botones, tooltips, menú, avisos de estado).
- Un archivo, una responsabilidad; ningún archivo de más de ~300 líneas (si `TileViewModel` o `CameraTileView` crecen, se reparten en archivos parciales por tema, uno por tema).
- Las vistas sin lógica de negocio. La interfaz no llama al sistema directamente (abrir carpetas: `App.Platform.Files`).
- Sonido: una sola cámara suena a la vez; como mucho 150 ms en cola (lo que se retrasa se descarta, nunca se acumula); la salida se cierra cuando no suena nada.
- Contraseñas nunca en texto claro en registros ni en la UI.
- Pruebas solo de código (lógica pura). No tocan datos reales.
- Compilar Desktop/App/solución siempre con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/`; las pruebas desde su salida normal. No ejecutar la aplicación ni tocar ningún Centinela en marcha.
- `git add` con rutas explícitas; commits con línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Cero advertencias de compilación.

---

### Task 1: Abrir carpetas y mostrar archivos (`IFileManager`)

**Files:**
- Create: `src/Centinela.Core/Platform/IFileManager.cs`
- Create: `src/Centinela.Platform/Windows/ExplorerFileManager.cs`, `src/Centinela.Platform/ShellFileManager.cs`
- Modify: `src/Centinela.Platform/PlatformServices.cs` (propiedad `Files`)
- Create: `tests/Centinela.Platform.Tests/FileManagerTests.cs`

**Interfaces:**
- Produces: `interface IFileManager { void OpenFolder(string folder); void Reveal(string path); }` (Reveal: una carpeta se abre; un archivo se muestra seleccionado donde el sistema lo permita, si no se abre su carpeta). `PlatformServices.Files` (`required IFileManager`). Windows: `ExplorerFileManager`; resto: `ShellFileManager`.

- [ ] **Step 1: Prueba** — las dos clases exponen la orden que lanzarían como función pura `static ProcessStartInfo CommandFor...` para poder probarla sin abrir nada:

```csharp
using Centinela.Platform;
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class FileManagerTests
{
    [Fact]
    public void Explorer_opens_folders_and_selects_files()
    {
        var folder = ExplorerFileManager.CommandForFolder(@"C:\Vídeos\Centinela");
        Assert.Equal(("explorer.exe", "\"C:\\Vídeos\\Centinela\""), (folder.FileName, folder.Arguments));
        var file = ExplorerFileManager.CommandForFile(@"C:\Imágenes\Centinela\a.png");
        Assert.Equal(("explorer.exe", "/select,\"C:\\Imágenes\\Centinela\\a.png\""), (file.FileName, file.Arguments));
    }

    [Fact]
    public void Shell_opens_the_folder_with_the_desktop_default()
    {
        var info = ShellFileManager.CommandForFolder("/home/ana/Videos/Centinela");
        Assert.Equal("/home/ana/Videos/Centinela", info.FileName);
        Assert.True(info.UseShellExecute);
    }
}
```

- [ ] **Step 2: Implementar**

`src/Centinela.Core/Platform/IFileManager.cs`:

```csharp
namespace Centinela.Core.Platform;

/// <summary>Shows folders and files in the system's file manager.</summary>
public interface IFileManager
{
    void OpenFolder(string folder);

    /// <summary>A folder is opened; a file is shown selected where the system can, otherwise its folder is opened.</summary>
    void Reveal(string path);
}
```

`src/Centinela.Platform/Windows/ExplorerFileManager.cs`:

```csharp
using System.Diagnostics;
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

public sealed class ExplorerFileManager : IFileManager
{
    public void OpenFolder(string folder) => Start(CommandForFolder(folder));

    public void Reveal(string path)
    {
        if (Directory.Exists(path)) Start(CommandForFolder(path));
        else if (File.Exists(path)) Start(CommandForFile(path));
    }

    internal static ProcessStartInfo CommandForFolder(string folder) => new("explorer.exe", $"\"{folder}\"");

    internal static ProcessStartInfo CommandForFile(string file) => new("explorer.exe", $"/select,\"{file}\"");

    static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (Exception) { /* nothing to show if the file manager cannot start */ }
    }
}
```

`src/Centinela.Platform/ShellFileManager.cs` (macOS `open` y Linux `xdg-open` los resuelve .NET con `UseShellExecute`):

```csharp
using System.Diagnostics;
using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>macOS and Linux: the desktop's default handler opens the folder (a file reveals its folder).</summary>
public sealed class ShellFileManager : IFileManager
{
    public void OpenFolder(string folder) => Start(CommandForFolder(folder));

    public void Reveal(string path)
    {
        if (Directory.Exists(path)) Start(CommandForFolder(path));
        else if (File.Exists(path) && Path.GetDirectoryName(path) is { } folder) Start(CommandForFolder(folder));
    }

    internal static ProcessStartInfo CommandForFolder(string folder) => new(folder) { UseShellExecute = true };

    static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (Exception) { /* nothing to show if the file manager cannot start */ }
    }
}
```

En `PlatformServices`: `public required IFileManager Files { get; init; }`; Windows `Files = new ExplorerFileManager()`, resto `Files = new ShellFileManager()` (también en la rama de pruebas de otros sistemas).

- [ ] **Step 3: Verificar** — pruebas de Platform pasan; solución compila con 0 advertencias.
- [ ] **Step 4: Commit** — `feat(platform): abrir carpetas y mostrar archivos en cada sistema`.

---

### Task 2: Estilos y barra de botones del mosaico; menú del clic derecho

**Files:**
- Create: `src/Centinela.Desktop/Styles/TileButtons.axaml`
- Modify: `src/Centinela.Desktop/App.axaml` (incluir el estilo)
- Create: `src/Centinela.Desktop/ViewModels/TileViewModel.Display.cs` (parcial: estado que ven los botones)
- Create: `src/Centinela.Desktop/Views/CameraTileView.Actions.cs` (parcial: botones y menú → eventos)
- Modify: `src/Centinela.Desktop/Views/CameraTileView.axaml`
- Create: `tests/Centinela.Desktop.Tests/TileDisplayTests.cs`

**Interfaces:**
- Consumes: `TileViewModel` (2a).
- Produces:
  - Parcial `TileViewModel` con observables: `bool LowQuality`, `string QualityText` («HD»/«SD»), `string QualityToolTip`, `bool MotionEnabled`, `string MotionToolTip`, `bool SmoothingOn`, `RecordingStatus Recording` (enum nuevo en `ViewModels/RecordingStatus.cs`: `Off, Connecting, Recording, Paused`), `string RecordText` («⏺»/«⏹»), `string RecordToolTip` («Grabar»/«Detener grabación»), `bool RecDotVisible`, `bool RecDotHollow`, `string RecDotToolTip`, `bool ShowFixButton`, `bool AudioCapable`, `bool HasAudio`, `bool AudioOn`, `bool AudioEnabled`, `string AudioText`, `string AudioToolTip`, `bool AudioButtonVisible`, `bool SnapshotBusy`, `bool IsZoomed`, `string ZoomText`; métodos `SetRecordingStatus(RecordingStatus)`, `SetMotionEnabled(bool)`, `SetLowQuality(bool)`, `SetSmoothing(bool)` (también pone `Session.Smoothing` si la sesión es propia), `DisableAudio(string reason)`.
  - `CameraTileView` eventos (todos `Action<CameraTileView>`): `QualityToggleRequested`, `AudioToggleRequested`, `MotionToggleRequested`, `SmoothingToggleRequested`, `SnapshotRequested`, `RecordRequested`, `EditRequested`, `DuplicateRequested`, `EncoderInfoRequested`, `DeleteRequested`; `FullscreenRequested` ya existe.

- [ ] **Step 1: Pruebas** de la lógica de textos (`TileDisplayTests.cs`): con un `TileViewModel` sobre una cámara sin URL (`new Camera { Brand = Brand.Custom }`, sin sesión: no abre red), comprobar:
  - `SetLowQuality(true)` → `QualityText == "SD"` y el tooltip «Calidad baja (flujo secundario): más fluida con mala conexión. Pulsa para calidad alta.»; `false` → «HD» y «Calidad alta (flujo principal). Pulsa para calidad baja si la imagen va a saltos.»
  - `SetRecordingStatus`: `Off` → `RecDotVisible == false`, `RecordText == "⏺"`, `RecordToolTip == "Grabar"`; `Recording` → punto macizo, tooltip «Grabando», `RecordText == "⏹"`, «Detener grabación»; `Paused` → punto hueco y «Grabación en pausa: reconectando…»; `Connecting` → hueco y «Grabación: conectando…».
  - `SetMotionEnabled(false)` → tooltip «Detección de movimiento: desactivada»; `true` → «… activada».
  - Audio: `AudioButtonVisible` solo si `AudioCapable && HasAudio`; `AudioOn` → texto «🔊» y tooltip «Silenciar»; apagado → «🔇» y «Activar sonido»; `DisableAudio("No hay salida de audio")` → `AudioEnabled == false` y tooltip ese texto.
  - `ShowFixButton` es true tras un `AuthFailed` en un mosaico con `Manage` (usar el método interno que aplica el estado; exponerlo `internal` para la prueba si hace falta).

- [ ] **Step 2: Estilos** — `Styles/TileButtons.axaml` con las clases `tile` y `tile-large` para `Button`, equivalentes a `TileButton`/`TileButtonLarge` de `src/Centinela.App/App.xaml`: 30×30 (grandes 40×40, fuente 18), margen 2,0 (grandes 3,0), fondo `#E0181818`, borde `#60FFFFFF` de 1, esquinas 5, texto blanco, fuente de emoji (`Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji`), 13 pt; pasar el ratón: fondo `#E0404040`, borde blanco; pulsado: `#F0080808`; desactivado: opacidad 0.45. Incluir en `App.axaml` con `<StyleInclude Source="avares://Centinela/Styles/TileButtons.axaml"/>` (el nombre del ensamblado es `Centinela`).

- [ ] **Step 3: `CameraTileView.axaml`** — añadir, como en `CameraTile.xaml`:
  - En la etiqueta del nombre: punto rojo de grabación (`Ellipse` 10×10, rojo macizo o hueco con borde rojo de 2 y opacidad 0.7 según `RecDotHollow`, tooltip `RecDotToolTip`, visible `RecDotVisible`), ojo tachado cuando `!MotionEnabled` (👁 11 pt al 80 % con una línea roja `#E53935` de 1.6 cruzada, tooltip «Detección de movimiento: desactivada»), ⏱ (ya está) y « 🔊» cuando `AudioOn`.
  - Botón «Editar» centrado bajo el estado, visible `ShowFixButton`.
  - Etiqueta de zoom abajo a la derecha (`ZoomText`, visible `IsZoomed`).
  - Barra de acciones `Border x:Name="Actions"` oculta hasta que el puntero entra en el mosaico. Mosaico pequeño: arriba a la derecha, margen 4, botones clase `tile`. Mosaico grande (`Big`): abajo centrado, margen inferior 24, relleno 6, esquinas 10, fondo `#C0000000`, botones clase `tile-large`. Botones en este orden: calidad («HD»/«SD», solo si `Big` y `Manage`, fuente Segoe UI negrita 12), sonido (visible `AudioButtonVisible`, activo `AudioEnabled`, el tooltip se ve aunque esté desactivado), 👁 (opacidad 1 o 0.6 según `MotionEnabled`, solo `Manage`), 📷 «Captura» (desactivado mientras `SnapshotBusy` o sin sesión), ⏺/⏹, ✎ «Editar» (solo `Manage`), 🗑 «Eliminar» (solo `Manage`).
  - Cambiar entre `tile` y `tile-large` con `Classes.tile-large="{Binding Big}"` / `Classes.tile="{Binding !Big}"` (o equivalente 11.3).

- [ ] **Step 4: `CameraTileView.Actions.cs`** — cada botón lanza su evento; el menú del clic derecho (`ContextMenu`) se construye en código con los mismos elementos y orden que la WPF: «Pantalla completa» (solo `Manage`), «Captura», «Grabar»/«Detener grabación» (sigue a `Recording`), y solo `Manage`: «Editar…», «Duplicar…», «Calidad de la cámara…», «Detección de movimiento» (con marca = `MotionEnabled`), «Suavizar imagen (1–2 s de retraso)» (marca = `SmoothingOn`, tooltip «Para cámaras con wifi irregular: la imagen va con retraso pero sin tirones.»), «Restablecer zoom» (visible solo con `IsZoomed`, llama al zoom de la Task 3), separador, «Eliminar». La marca de las opciones la pone el dueño (no se alterna sola al pulsar), igual que en la WPF. Mostrar/ocultar la barra en `PointerEntered`/`PointerExited`.

- [ ] **Step 5: `TileViewModel.Display.cs`** — implementar las propiedades y métodos de «Produces» con los textos exactos de la WPF (`CameraTile.xaml.cs`: `SetLowQuality`, `SetRecordingStatus`, `ShowRecordingDot`, `SetMotionEnabled`, `SetSmoothing`, `SetAudio`, `DisableAudio`, `UpdateAudioButton`). `ShowFixButton` = `Manage` y el último estado es `AuthFailed` (se apaga al llegar a `Playing`): tocar `ShowState` del archivo principal para mantenerlo. `HasAudio` se actualiza con `Session.InfoAvailable` (publicado al hilo de interfaz, suscrito/desuscrito como los otros eventos) leyendo `Session.Info?.AudioCodec is not null`; si `AudioOn` y deja de haber audio, lanzar el evento `AudioLost` (nuevo, `Action<TileViewModel>`).

- [ ] **Step 6: Verificar** — pruebas de Desktop pasan; solución con 0 advertencias.
- [ ] **Step 7: Commit** — `feat(desktop): botones y menú de cada cámara`.

---

### Task 3: Zoom digital

**Files:**
- Create: `src/Centinela.Desktop/Video/ZoomPanBehavior.cs`
- Modify: `src/Centinela.Desktop/Views/CameraTileView.axaml.cs` (rueda, arrastre, tecla 0 vía el dueño, tamaño de decodificación × escala)
- Modify: `src/Centinela.Desktop/ViewModels/TileViewModel.Display.cs` (`EnableZoom`)
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml.cs` y `FullscreenWindow.axaml.cs` (tecla 0 sobre el mosaico bajo el puntero / pantalla completa)

**Interfaces:**
- Consumes: `ZoomState` (Core: `Resize(w,h)`, `SetContent(x,y,w,h)`, `WheelAt(x,y,notches)`, `Pan(dx,dy)`, `Reset()`, `IsZoomed`, `Scale`, `OffsetX`, `OffsetY`), `VideoSurface.ContentRect` (2a).
- Produces: `sealed class ZoomPanBehavior(VideoSurface video, Control host) { bool IsZoomed; double Scale; void WheelAt(Point p, double notches); bool BeginPan(Point p); void PanTo(Point p); void EndPan(); void Reset(); void Apply(); event Action? Changed; }`; `CameraTileView.ResetZoom()`; `TileViewModel.EnableZoom` (= `Big`, y en pantalla completa `true`).

Comportamiento (igual que `CameraTile.xaml.cs`: `OnMouseWheel`, `SyncZoomView`, `ApplyZoom`, pan con el botón izquierdo mientras hay zoom, un pan no es clic ni arrastre de reordenar):
- Rueda: `ZoomState.WheelAt` en el punto del puntero, 1 muesca = 120 en Windows → en Avalonia `e.Delta.Y` ya viene en muescas.
- Transformación: `MatrixTransform` sobre `VideoSurface` con `Matrix(s,0,0,s,(s−1)·o.X+OffsetX,(s−1)·o.Y+OffsetY)`, donde `o` es la esquina de `ContentRect`. Una sola instancia por mosaico, actualizada en su sitio.
- Calidad de escalado: `RenderOptions.SetBitmapInterpolationMode(video, IsZoomed ? HighQuality : MediumQuality)`.
- Etiqueta `ZoomText` con formato «{0:0.#}×» en es-ES; «Restablecer zoom» visible solo con zoom.
- Tamaño de decodificación: `SetTargetSize(ancho·escalado·Scale, alto·escalado·Scale)` (una imagen con zoom se decodifica más grande; FFmpeg nunca pasa del tamaño nativo).
- Al dejar de ser grande (`Big` pasa a false) o al hacer de «tapa» en un relevo: `Reset()` y `EnableZoom = false` (pendiente de la 2a).
- Tecla 0 (y 0 del teclado numérico): en la ventana principal quita el zoom del mosaico bajo el puntero; en pantalla completa, el de su mosaico.

- [ ] **Step 1:** Implementar `ZoomPanBehavior` y conectar en la vista. **Step 2:** compilar con 0 advertencias. **Step 3: Commit** — `feat(desktop): zoom digital en las cámaras grandes y a pantalla completa`.

---

### Task 4: Grabación y capturas; «Grabaciones» y «Grabar todas»; enlace en la línea de estado

**Files:**
- Create: `src/Centinela.Desktop/Services/Recording/RecordingController.cs` (puerto de `src/Centinela.App/RecordingController.cs`)
- Create: `src/Centinela.Desktop/Services/Recording/SnapshotService.cs`
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml(.cs)`, `ViewModels/MainViewModel.cs`
- Create: `tests/Centinela.Desktop.Tests/RecordAllTests.cs`

**Interfaces:**
- Produces:
  - `RecordingController` con la misma API que la WPF (`Start(Camera)`, `Stop(Guid)`, `StopWithNotice(Guid)`, `StopAllWithNotice()`, `ShutdownAsync()`, `IsRecording`, `AnyRecording`, `StatusOf`, eventos `StatusChanged`, `ErrorOccurred`, `SessionStateChanged`, `Notify`, `Failure`), usando `RecordingStatus` de la Task 2.
  - `static class RecordAll { static IReadOnlyList<Guid> Targets(IEnumerable<Camera> all, IEnumerable<Guid> onScreen, bool allCameras); }` — puro.
  - `sealed class SnapshotService { Task<bool> SaveAsync(TileViewModel tile, string path, Func<bool> cancelled); }`.
  - `MainViewModel`: `string RecordAllText` («⏺ Grabar todas» / «⏹ Detener todas»), `string? StatusRevealPath`; método `Notify(string message, string? revealPath)`.

- [ ] **Step 1: `RecordingController`** — copiar `src/Centinela.App/RecordingController.cs` con exactamente estos cambios: namespace `Centinela.Desktop.Services.Recording`; constructor sin parámetros (`public sealed class RecordingController`); cada `dispatcher.BeginInvoke(…)` → `UiThread.Post(…)`; quitar `using System.Windows.Threading;` y el `enum RecordingStatus` (usa el de `Centinela.Desktop.ViewModels`). Mismos textos.

- [ ] **Step 2: `RecordAll` + prueba** — puerto de `ToggleRecordAll`: si no es `allCameras`, solo las cámaras en pantalla; el orden es el de `all`.

```csharp
public class RecordAllTests
{
    [Fact]
    public void Toolbar_records_the_cameras_on_screen_and_the_tray_records_all()
    {
        var cams = Enumerable.Range(0, 4).Select(i => new Camera { Name = $"C{i}" }).ToList();
        var onScreen = new[] { cams[2].Id, cams[0].Id };
        Assert.Equal([cams[0].Id, cams[2].Id], RecordAll.Targets(cams, onScreen, allCameras: false));
        Assert.Equal(cams.Select(c => c.Id), RecordAll.Targets(cams, onScreen, allCameras: true));
    }
}
```

- [ ] **Step 3: `SnapshotService`** — puerto de `CameraTile.SaveMainStreamSnapshotAsync`: si el mosaico es del flujo principal, `Session.SaveSnapshotAsync(path)`; si es del secundario, abre una sesión del principal a 2×2, espera hasta 3 s (o `AuthFailed`, o cancelado) a tener fotograma y guarda de ahí; si no, del secundario. La sesión temporal se desecha en segundo plano.

- [ ] **Step 4: Ventana principal** — barra: «➕ Añadir cámara» (Task 7), separador, «Vista:» y desplegable (ya), separador, «📁 Grabaciones» (crea la carpeta y la abre con `App.Platform.Files.OpenFolder(AppPaths.RecordingsDirectory)`), «⏺ Grabar todas» (texto `RecordAllText`). La línea de estado se pulsa para mostrar `StatusRevealPath` con `App.Platform.Files.Reveal`. Cada mosaico: ⏺ → `ToggleRecording` (con la cámara actual del repositorio); 📷 → nombre `AppPaths.SnapshotFile(camera.Name, DateTime.Now)`, `SnapshotBusy` mientras guarda, mensaje «Captura guardada: {ruta}» (con enlace) o «Aún no hay imagen para capturar.» o «No se pudo guardar la captura: {mensaje}». Los mosaicos y las pantallas completas reflejan `StatusChanged` con `SetRecordingStatus`, también los creados después (`TileFactory.Created` les pone `StatusOf`). `Failure` y `Notify` del controlador van a la línea de estado. Al cerrar, la espera acotada incluye `_recordings.ShutdownAsync()`.

- [ ] **Step 5:** pruebas y compilación con 0 advertencias. **Step 6: Commit** — `feat(desktop): grabación, capturas y carpeta de grabaciones`.

---

### Task 5: Sonido con OpenAL

**Files:**
- Modify: `src/Centinela.Desktop/Centinela.Desktop.csproj` (paquetes Silk.NET)
- Create: `src/Centinela.Desktop/Services/Audio/OpenAlAudioOutput.cs`, `src/Centinela.Desktop/Services/Audio/AudioController.cs`
- Create: `tests/Centinela.Desktop.Tests/SampleConversionTests.cs`

**Interfaces:**
- Consumes: `IAudioSink`, `AudioFormat` (Media: estéreo intercalado float32 a 48 kHz), `AudioCoordinator<T>` (Core), `TileViewModel` (`AudioCapable`, `AudioOn`, `SetAudio(IAudioSink?)`, `DisableAudio`, `AudioLost`, y el evento de fallo de la sesión).
- Produces:
  - `sealed class OpenAlAudioOutput : IAudioSink, IDisposable { static OpenAlAudioOutput? TryCreate(); void Write(ReadOnlySpan<float>); void Clear(); event Action<OpenAlAudioOutput, string>? Failed; }`.
  - `static class SampleConversion { static void ToPcm16(ReadOnlySpan<float> source, Span<short> target); }` (puro, recorta a ±1).
  - `sealed class AudioController { void Toggle(TileViewModel tile); void Release(TileViewModel tile); event Action<string>? Notice; }` — puerto de `MainWindow.Audio.cs`.
  - En `TileViewModel`: `void SetAudio(IAudioSink? sink)` (solo sesiones propias: una sesión compartida nunca suena), evento `AudioFailed(TileViewModel, string)` publicado desde `Session.AudioFailed` (ignorado si ya no suena o está desechado).

Reglas (las de `AudioOutput.cs`): la salida se abre al primer 🔊 y se cierra cuando no suena nada; una sola cámara a la vez (`AudioCoordinator`); como mucho **150 ms** en cola: si hay más, se vacía antes de añadir (se descarta, nunca se retrasa); si el dispositivo falla o se desconecta, se silencia la cámara, se avisa «No se pudo reproducir el audio de «{nombre}»» (el vídeo sigue) y el siguiente 🔊 abre el dispositivo actual; sin dispositivo, todos los botones de sonido quedan desactivados con el tooltip «No hay salida de audio» y la línea de estado dice «No hay salida de audio en este equipo.»

- [ ] **Step 1: Prueba** de `SampleConversion` (0 → 0, 1 → 32767, −1 → −32767, 2 → 32767, −3 → −32767, 0.5 → 16384 redondeado).
- [ ] **Step 2: `OpenAlAudioOutput`** — con `Silk.NET.OpenAL`: `ALContext.GetApi(soft: true)` y `AL.GetApi(soft: true)`; abrir el dispositivo por defecto, crear el contexto y hacerlo actual; una fuente; en cada `Write` (hilo de audio, bajo un candado): desencolar los búferes ya reproducidos para reutilizarlos; si lo encolado supera 150 ms, parar la fuente y desencolarlo todo; convertir a PCM de 16 bits estéreo (`SampleConversion`) y encolar un búfer a 48 000 Hz; si la fuente no está sonando, `SourcePlay`. Comprobar la desconexión con la extensión `ALC_EXT_disconnect` (`ALC_CONNECTED`, 0x313) cada vez que se escribe; si se perdió, lanzar `Failed` una vez. `Dispose` libera fuente, búferes, contexto y dispositivo sin lanzar. Si alguna llamada de Silk.NET 2.23 se llama distinto, usar la equivalente y anotarlo.
- [ ] **Step 3: `AudioController`** — puerto de `MainWindow.Audio.cs` (Toggle, Release, cierre de la salida cuando no suena nada, fallo del dispositivo, `DisableAudio` en todos los mosaicos que pueden sonar cuando no hay salida, aviso por `Notice`). Mosaicos que pueden sonar: grandes de la rejilla del flujo principal y pantallas completas. Liberar el sonido al retirar un mosaico, al dejar de ser grande, al hacer de «tapa» y al cerrar una pantalla completa.
- [ ] **Step 4:** pruebas y compilación. **Step 5: Commit** — `feat(desktop): sonido de una cámara a la vez con OpenAL`.

---

### Task 6: Acciones de cámara: HD/SD, suavizar, 👁, eliminar; pantallas completas

**Files:**
- Create: `src/Centinela.Desktop/Services/Cameras/CameraActions.cs`
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml.cs`, `Services/Tiles/TileHost.cs`, `Views/FullscreenWindow.axaml.cs`

**Interfaces:**
- Produces: `sealed class CameraActions(CameraRepository cameras, Func<Guid, IEnumerable<TileViewModel>> tilesOf, StreamLeases leases) { void ToggleQuality(Guid id); void ToggleSmoothing(Guid id); void ToggleMotion(Guid id); }` — cada una cambia la opción de la cámara actual, guarda y la aplica a todos sus mosaicos (y, en suavizar, también a sus sesiones compartidas con `leases.SessionsFor`). HD/SD además pide reconstruir la vista (evento `ViewChanged`).
- Pantallas completas: la ventana principal lleva la lista de las abiertas; `TilesOf` las incluye; `DisposeTilesOf` (cámara editada o borrada) también las cierra; el cambio de «Mostrar estadísticas» también les llega.
- Eliminar: pregunta «¿Eliminar la cámara «{nombre}»?» (`MessageDialog.ConfirmAsync`), para su grabación con aviso, la quita, retira sus mosaicos y pantallas completas, guarda, limpia la cámara elegida como principal y de «Dos principales» si era ella, y reconstruye.

- [ ] **Step 1:** implementar y conectar (👁 solo guarda `MotionEnabled` y actualiza el indicador; la detección llega en 2d). **Step 2:** compilación con 0 advertencias. **Step 3: Commit** — `feat(desktop): calidad HD/SD, suavizar, detección y eliminar`.

---

### Task 7: Diálogo «Añadir / Editar cámara» con «Probar»; Añadir, Editar y Duplicar

**Files:**
- Create: `src/Centinela.Desktop/ViewModels/CameraForm.cs` (puro: validar y aplicar el formulario)
- Create: `src/Centinela.Desktop/Views/AddCameraDialog.axaml`, `src/Centinela.Desktop/Views/AddCameraDialog.axaml.cs`
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml(.cs)` (botón «➕ Añadir cámara», «Añade tu primera cámara» en el estado vacío, Editar/Duplicar/Editar desde el botón de error)
- Create: `tests/Centinela.Desktop.Tests/CameraFormTests.cs`

**Interfaces:**
- Produces:
  - `sealed record CameraFields(Brand Brand, string Name, string Host, string Port, string User, string Password, string MainUrl, string SubUrl, bool UseUdp)`.
  - `sealed class CameraForm(Camera camera) { string UrlHost; int UrlPort; bool TryApply(CameraFields fields, out string error, out string mainUrl, out string subUrl); static string HintFor(Brand brand); }` — reglas y textos exactos de `AddCameraDialog.TryReadForm` y `Brand_Changed`: nombre obligatorio («Pon un nombre a la cámara.»), IP obligatoria salvo «Otra» («La IP es obligatoria.»), URL principal obligatoria en «Otra» («La URL principal es obligatoria.»), puerto 1–65535 («Puerto no válido.»); si cambian la IP o el puerto, las URL de «Avanzado» se mueven con `StreamUrlBuilder.RebaseOverride` y se devuelven para mostrarlas; el usuario «admin» se propone al elegir Imou (lo hace la vista).
  - `AddCameraDialog(Camera? initial, bool isNew)` con `Camera Result`; `ShowDialog<bool>(owner)`.

- [ ] **Step 1: Pruebas** de `CameraForm`: cada error con su texto; que un cambio de IP mueve las URL override; que al aplicar se rellenan todos los campos de la copia y `Result` no es la cámara original (`Clone`); `HintFor` de cada marca con los textos de `Brand_Changed`.
- [ ] **Step 2: Diálogo** — mismo aspecto y textos que `AddCameraDialog.xaml` («Añadir cámara» / «Editar cámara», Marca [Tapo, Imou, Otra (RTSP)], Nombre, IP, Puerto RTSP, Usuario, Contraseña (con `PasswordChar`), «Avanzado» con URL principal «(opcional salvo en «Otra»)», URL secundaria «(opcional)», «Usar UDP (menos retardo, puede mostrar artefactos)», pista gris, vista previa negra de 180 de alto, líneas de resultado, botones Probar / Guardar / Cancelar). «Probar»: dos sesiones (secundaria decodificada a 640×360 mostrada con un `VideoSurface`, principal sin decodificar solo para sus datos), líneas «Principal: …» y «Secundaria: …» con `StreamInfo.Describe()` o el error traducido corto (`ErrorText`, nunca el detalle), 10 s máximo («sin respuesta en 10 s»), sesiones desechadas fuera del hilo de interfaz, todo como `AddCameraDialog.xaml.cs`.
- [ ] **Step 3: Ventana principal** — Añadir (orden al final), Duplicar (`camera.Duplicate()` en el diálogo de añadir), Editar (para su grabación, reemplaza la cámara, retira sus mosaicos y pantallas completas, guarda, reconstruye; las notificaciones de error de esa cámara se olvidan en 2d).
- [ ] **Step 4:** pruebas y compilación. **Step 5: Commit** — `feat(desktop): añadir, editar y duplicar cámaras con «Probar»`.

---

### Task 8: Pendientes de la fase 2a

**Files:** `src/Centinela.Desktop/Video/VideoSurface.cs`, `Views/CameraTileView.axaml.cs`, `Services/Tiles/TileHost.cs`, `ViewModels/TileViewModel.cs`, `tests/Centinela.Desktop.Tests/LayoutControllerTests.cs`

- [ ] `VideoSurface`: una sola petición de fotograma a la vez (`_frameRequested`); no pedir fotogramas sin sesión; en `OnFrame`, si ya no está colocado, no copiar nada.
- [ ] `CameraTileView`: al volver a colocarse, reiniciar el temporizador de estadísticas; recalcular el tamaño de decodificación con `TopLevel.ScalingChanged`; un botón soltado fuera del mosaico no es clic (`Bounds.Contains(e.GetPosition(this))`).
- [ ] `TileHost`: si el mosaico nuevo ya está fallando al crearse (`Reconnecting`/`AuthFailed`), retirar la tapa en el acto; parar el temporizador de 5 s al retirar.
- [ ] `TileViewModel.Start()`: documentar que hay que suscribirse a `StreamFailed` antes.
- [ ] `LayoutControllerTests`: en «Dos principales», comprobar qué cámara grande se sustituye (la más antigua).
- [ ] Compilación con 0 advertencias y pruebas. **Commit** — `fix(desktop): pendientes de la fase 2a`.

---

### Task 9: Comprobación de la fase 2b

- [ ] Pruebas de Core, Platform, Media y Desktop pasan; solución con 0 advertencias; nada de APIs de Windows en Desktop.
- [ ] **Prueba del usuario** (cerrando antes el Centinela WPF): botones y menú de cada cámara, HD/SD, zoom (rueda, arrastrar, 0, «Restablecer zoom»), sonido de una sola cámara y silencio, captura (y el enlace de la línea de estado), grabar una y todas, «Grabaciones», suavizar, 👁, añadir/editar/duplicar/eliminar con «Probar».
