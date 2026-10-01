# Centinela v1.4 — Fase 3: Linux — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centinela funciona en Linux x64 (probado por el usuario en Ubuntu 24.04 con GNOME, en una VM de Proxmox en la red de las cámaras) con la misma funcionalidad que en Windows: contraseñas en el llavero (Secret Service) o en un archivo cifrado, una sola instancia, autoarranque, bandeja (o minimizar si no hay), notificaciones del sistema, carpetas XDG, ping en «Detectar otras redes», FFmpeg con VAAPI, y un `.deb` que el usuario construye en la propia VM.

**Architecture:** Como en las fases anteriores: interfaces en `Centinela.Core.Platform`, implementaciones de Linux en `Centinela.Platform/Linux/` (un archivo por pieza), elegidas en `PlatformServices.Create`. D-Bus con `Tmds.DBus.Protocol` (la misma versión que ya trae Avalonia: 0.21.3). La bandeja de Linux usa el `TrayIcon` de Avalonia (StatusNotifierItem) y vive en `Centinela.Desktop/Services/Tray/AvaloniaTray.cs` (implementa la `ITray` de la 2d); los avisos del sistema van por `org.freedesktop.Notifications`. Lo que se puede probar sin Linux (textos, formatos, rutas, el protocolo de instancia única con sockets Unix, que también funcionan en Windows) se prueba en Windows; lo demás lo prueba el usuario en la VM.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22, Tmds.DBus.Protocol 0.21.3, FFmpeg 9.0 LGPL (BtbN, linux64 shared), OpenAL Soft (Silk.NET.OpenAL.Soft.Native, ya trae `linux-x64`), `dpkg-deb`.

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (§2 plataforma, §4 paquetes, «Errores y casos especiales», §6 fase 3). Pendientes: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md` (secciones «Fase 3», «Fases 3–4», «Arrastrados de la fase 2d › Fases 3–4»).

**Respuestas del usuario (1 de octubre):** prueba en Ubuntu 24.04 con GNOME (tiene la extensión de iconos de la bandeja activada); construye el `.deb` en la propia VM (SDK de .NET 10 instalado allí, `tools/build-deb.sh`).

**Fuera de esta fase:** macOS (fase 4), GitHub Actions y firma (fase 5), README por sistema y versión 1.4.0 (fase 6: hasta entonces el `.deb` lleva la versión de `Directory.Build.props`).

## Global Constraints

- .NET 10; Avalonia 11.3.22. Único paquete NuGet nuevo: `Tmds.DBus.Protocol` 0.21.3 en `Centinela.Platform`.
- Nada cambia en Windows: mismo comportamiento y mismos textos (las pruebas de Windows siguen pasando).
- Textos visibles en español, con el mismo tono que los de Windows; los nuevos los fija este plan.
- Un archivo, una responsabilidad; ningún archivo de más de ~300 líneas. Lo de Linux solo en `Centinela.Platform/Linux/` (y la bandeja Avalonia en `Centinela.Desktop/Services/Tray/`); `Centinela.Desktop` no llama a APIs nativas.
- Contraseñas nunca en texto claro en registros, avisos, mensajes ni archivos salvo el almacén del sistema o el archivo cifrado (clave `0600`, creada sin ventana de permisos abiertos).
- Con `CENTINELA_DATA_DIR`: nada real del sistema (sin llavero real, sin autoarranque real).
- Las pruebas se ejecutan en Windows: solo lógica pura, archivos temporales y sockets Unix en carpetas temporales. Ninguna prueba usa D-Bus, el llavero, la bandeja, la red ni las carpetas reales del usuario.
- Compilar y probar Desktop/solución con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/` (Core/Media/Platform sin ese parámetro). No ejecutar la aplicación.
- `git add` con rutas explícitas (nunca los `README.sync-conflict-*.md`); commits con línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Los scripts `.sh` con finales de línea LF (añadir `*.sh text eol=lf` a `.gitattributes`).
- Cero advertencias de compilación (incluido el analizador de plataforma: lo de Linux con `[SupportedOSPlatform("linux")]`).

---

### Task 1: Pendientes de la fase 3 (archivo de claves, carpetas XDG, gestor de archivos, mensajes)

**Files:**
- Modify: `src/Centinela.Platform/FileSecretStore.cs`, `src/Centinela.Core/Platform/PlatformFolders.cs`, `src/Centinela.Platform/ShellFileManager.cs`, `src/Centinela.Platform/PlatformServices.cs`, `src/Centinela.Desktop/Services/Audio/OpenAlAudioOutput.cs`
- Modify: `tests/Centinela.Platform.Tests/` (pruebas de `FileSecretStore` y del gestor de archivos), `tests/Centinela.Core.Tests/PlatformFoldersTests.cs`

- [ ] **`FileSecretStore`** (pendientes «Fase 3»): crear la clave con `new FileStream(keyFile, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UserRead | UserWrite })` (en Windows sin `UnixCreateMode`); si otro proceso la creó a la vez (`IOException` por existir), leer esa; **nunca** sobrescribir una clave existente aunque tenga un tamaño incorrecto (en ese caso `Protect` lanza `CryptographicException("El archivo de claves de Centinela está dañado.")` y `Unprotect` devuelve null); `Unprotect` **no crea** la clave: si no existe o no se puede leer, devuelve null. Pruebas: dos almacenes sobre la misma ruta comparten la clave; `Unprotect` sin archivo de clave devuelve null y no lo crea; un archivo de clave de 10 bytes no se sobrescribe.
- [ ] **`PlatformFolders`**: ignorar `XDG_DATA_HOME` relativo (no empieza por `/`) y entradas relativas de `user-dirs.dirs`; quitar la barra final antes de añadir `/centinela`; `ReadUserDirs` decide por `OsInfo.Current` (no `OperatingSystem.IsLinux()` directamente). Pruebas con `For(OsKind.Linux, ...)`.
- [ ] **`ShellFileManager`**: `OpenFolder` de una carpeta que no existe no hace nada; un solo `Start` (quitar la duplicación); en Linux usar `xdg-open` explícito (`new ProcessStartInfo("xdg-open") { ArgumentList = { folder } }`), en macOS `open` (y `open -R archivo` en `Reveal`, que muestra el archivo seleccionado). Pruebas de las órdenes construidas (funciones puras `CommandForFolder(OsKind, string)` / `CommandForFile(OsKind, string)`).
- [ ] **`PlatformServices`**: el mensaje para un sistema no soportado pasa a «Centinela todavía no está disponible en este sistema.» (la rama Linux real llega en la Task 7).
- [ ] **OpenAL** (pendiente «Fases 3–4»): limpiar los errores antiguos (`GetError` hasta `NoError`) antes de cada operación cuyo error se comprueba, para no atribuir a esta un error anterior.
- [ ] Pruebas de Core, Platform y Desktop; solución con 0 advertencias. **Commit** — `fix(platform): pendientes de Linux (claves, XDG, archivos)`.

---

### Task 2: D-Bus y Secret Service

**Files:**
- Modify: `src/Centinela.Platform/Centinela.Platform.csproj` (`Tmds.DBus.Protocol` 0.21.3)
- Create: `src/Centinela.Platform/Linux/SessionBus.cs` (conexión perezosa al bus de sesión, con tiempo máximo; `bool IsAvailable`; `bool HasOwner(string name)`)
- Create: `src/Centinela.Platform/Linux/SecretServiceClient.cs` (las llamadas D-Bus de `org.freedesktop.secrets`)
- Create: `src/Centinela.Platform/Linux/LinuxSecretStore.cs`
- Create: `src/Centinela.Platform/Linux/SecretServiceNames.cs` (puro: atributos, etiqueta, valor guardado)
- Create: `tests/Centinela.Platform.Tests/LinuxSecretStoreTests.cs`

**Interfaces:**
- Produces:
  - `static class SecretServiceNames { const string Application = "centinela"; static IReadOnlyDictionary<string, string> Attributes(Guid cameraId); static string Label(Guid cameraId); static string StoredValue(Guid cameraId); static Guid? CameraOf(string storedValue); }` — atributos `app=centinela`, `camera=<Id "D">`; etiqueta «Centinela: contraseña de la cámara {Id}»; valor guardado `secret-service:<Id>`.
  - `interface ISecretBackend { bool Store(Guid cameraId, string password); string? Lookup(Guid cameraId); void Delete(Guid cameraId); }` (interno; `SecretServiceClient` lo implementa; las pruebas usan uno falso).
  - `sealed class LinuxSecretStore(ISecretBackend? backend, FileSecretStore fallback) : ISecretStore` con `bool UsesFileFallback` — `Protect`: vacío → ""; con llavero disponible y `Store` correcto → `secret-service:<Id>`; si no, el archivo cifrado (`file:`) y `UsesFileFallback = true`. `Unprotect`: `secret-service:` → `Lookup` (null si no está o no hay llavero); `file:` → el archivo; otros (DPAPI de Windows, `keychain:` de Mac) → null. `Forget`: borra el elemento del llavero si el valor es `secret-service:`.

`SecretServiceClient` (no se prueba en Windows): sesión `plain` con `OpenSession`; colección por defecto (alias `default`); `Unlock` si está bloqueada (si devuelve un *prompt*, `Prompt` y esperar `Completed` hasta 60 s); `CreateItem` con `replace = true`, atributos y la etiqueta; `SearchItems` + `GetSecret`; `Delete`. Cada llamada síncrona con un tiempo máximo de 5 s (fuera del hilo de interfaz: `Task.Run(...).Wait(...)`) y cualquier fallo → `false`/`null` (nunca lanza al llamador).

- [ ] **Step 1: Pruebas** con un `ISecretBackend` falso en memoria y un `FileSecretStore` en carpeta temporal: con llavero, `Protect` devuelve `secret-service:<Id>` y `Unprotect` recupera la contraseña; si `Store` falla, devuelve `file:...`, `UsesFileFallback` es true y se puede leer; sin llavero (`backend` null) igual que el fallo; `Unprotect` de `dpapi:`/sin prefijo/`keychain:` → null; `Forget` borra solo los `secret-service:`; los nombres de `SecretServiceNames`.
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(platform): contraseñas en el llavero de Linux (Secret Service)`.

---

### Task 3: Instancia única con socket Unix

**Files:**
- Create: `src/Centinela.Platform/Unix/UnixSingleInstance.cs` (Linux y, en la fase 4, macOS)
- Create: `tests/Centinela.Platform.Tests/UnixSingleInstanceTests.cs`

**Interfaces:**
- Produces: `sealed class UnixSingleInstance(string socketPath) : ISingleInstance, IDisposable` y `static string UnixSingleInstance.PathFor(string? runtimeDir, string dataDir, string suffix)` — `$XDG_RUNTIME_DIR/centinela{suffix}.sock` si hay `XDG_RUNTIME_DIR` absoluto; si no, `<dataDir>/centinela{suffix}.sock`.

Protocolo: `TryClaim`: intentar conectar al socket; si conecta, enviar la línea `show\n`, cerrar y devolver `false`; si no (no existe o nadie escucha), borrar el archivo viejo, crear la carpeta, `Bind` + `Listen` y devolver `true` (si el `Bind` falla porque otro proceso ganó la carrera, volver a intentar conectar una vez). `ListenForActivation(onActivate)`: un hilo de fondo acepta conexiones y por cada línea `show` llama a `onActivate`. Repetidos: conservan el primer resultado (como `WindowsSingleInstance`). `Dispose`: cierra y borra el socket.

- [ ] **Step 1: Pruebas** (en Windows, con una carpeta temporal; `AF_UNIX` funciona en Windows 10+): la primera instancia reclama; una segunda con la misma ruta no reclama y la primera recibe una activación; un archivo de socket abandonado (sin nadie escuchando) no impide reclamar; `PathFor` con y sin `XDG_RUNTIME_DIR` (y con uno relativo, que se ignora).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(platform): instancia única con socket Unix`.

---

### Task 4: Autoarranque en Linux

**Files:**
- Create: `src/Centinela.Platform/Linux/LinuxAutoStart.cs`, `src/Centinela.Platform/Linux/DesktopEntry.cs` (puro)
- Create: `tests/Centinela.Platform.Tests/LinuxAutoStartTests.cs`

**Interfaces:**
- Produces: `static class DesktopEntry { static string AutoStart(string exePath); static string Quote(string path); }` y `sealed class LinuxAutoStart(string autostartDir, string exePath) : IAutoStart` con `static string DefaultDirectory(Func<string, string?> env, string home)` (`$XDG_CONFIG_HOME/autostart` si es absoluto, si no `~/.config/autostart`).

Contenido del archivo `centinela.desktop` (diseño §2):

```
[Desktop Entry]
Type=Application
Name=Centinela
Comment=Visor de cámaras IP
Exec=<ruta entre comillas si hace falta> --tray
Icon=centinela
Terminal=false
X-GNOME-Autostart-enabled=true
```

`Quote`: comillas dobles y las barras invertidas, comillas, `$` y acentos graves escapados según la especificación de las entradas de escritorio, solo si la ruta tiene espacios o caracteres reservados. `IsEnabled`: el archivo existe, su `Exec` apunta a este programa y no tiene `Hidden=true`. `Enable`: escribe el archivo de forma atómica (temporal y mover). `Disable`: lo borra. `Migrate`: si existe y apunta a otra ruta de Centinela (otra instalación), lo reescribe con la actual; devuelve si cambió algo.

- [ ] **Step 1: Pruebas** en carpeta temporal: el contenido exacto; ruta con espacios entre comillas; `Enable` → `IsEnabled`; `Disable` → no; `Migrate` corrige la ruta; `DefaultDirectory` con y sin `XDG_CONFIG_HOME` (y relativo ignorado).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(platform): arrancar al iniciar sesión en Linux`.

---

### Task 5: Notificaciones y disponibilidad de la bandeja en Linux

**Files:**
- Create: `src/Centinela.Core/Platform/INotifier.cs`, `src/Centinela.Core/Platform/ITrayAvailability.cs`
- Create: `src/Centinela.Platform/Linux/LinuxNotifier.cs`, `src/Centinela.Platform/Linux/NotificationRequest.cs` (puro), `src/Centinela.Platform/Linux/LinuxTrayAvailability.cs`
- Create: `tests/Centinela.Platform.Tests/LinuxNotifierTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace Centinela.Core.Platform;

/// <summary>System notifications (outside the tray icon). Call on the UI thread; onClick runs on the UI thread.</summary>
public interface INotifier : IDisposable
{
    void Show(string title, string text, Action? onClick = null);
}

/// <summary>Whether the desktop can show a tray icon right now.</summary>
public interface ITrayAvailability
{
    bool IsAvailable { get; }
}
```

- `LinuxNotifier(SessionBus bus, SynchronizationContext ui)`: `org.freedesktop.Notifications.Notify("Centinela", 0, "centinela", título, texto, ["default", "Abrir"], {}, -1)`; guarda la acción por el id devuelto; con la señal `ActionInvoked(id, "default")` ejecuta la acción en el hilo de interfaz; con `NotificationClosed(id, _)` la olvida. Sin servicio de notificaciones: no hace nada (nunca lanza). `NotificationRequest` (puro) arma los argumentos (y recorta el texto a 1000 caracteres).
- `LinuxTrayAvailability(SessionBus bus)`: `IsAvailable` = hay dueño del nombre `org.kde.StatusNotifierWatcher` en el bus de sesión (lo que usa el `TrayIcon` de Avalonia en Linux).

- [ ] **Step 1: Pruebas** de `NotificationRequest` (argumentos exactos, acciones, recorte, título vacío → «Centinela»).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias. **Step 5: Commit** — `feat(platform): notificaciones de Linux`.

---

### Task 6: Bandeja de Avalonia y comportamiento sin bandeja

**Files:**
- Create: `src/Centinela.Desktop/Services/Tray/AvaloniaTray.cs` (implementa `ITray` con `TrayIcon` y `NativeMenu` de Avalonia; `ShowNotice` usa el `INotifier`)
- Modify: `src/Centinela.Platform/PlatformServices.cs` (propiedades `INotifier? Notifier` y `ITrayAvailability? TrayAvailability`; en Windows las dos son `null` y se usa la bandeja de siempre)
- Modify: `src/Centinela.Desktop/Views/MainWindow.Tray.cs`, `MainWindow.axaml(.cs)`, `MainWindow.Motion.cs`, `MainWindow.Recording.cs`/`MainWindow.Updates.cs` (donde se usa `_tray?.ShowNotice` directamente)
- Create: `tests/Centinela.Desktop.Tests/SystemNoticeTests.cs`

Comportamiento:
- Elegir la bandeja: `App.Platform.CreateTray?.Invoke()` (Windows); si no, en Linux/macOS con `TrayAvailability?.IsAvailable == true`, `new AvaloniaTray(App.Platform.Notifier)`; si no, sin bandeja.
- `AvaloniaTray`: icono con `WindowIcon` desde el `TrayImage` (convertir a un `Bitmap` de Avalonia), tooltip; menú `NativeMenu` reconstruido en cada `SetMenu` (las casillas con `ToggleType = CheckBox`); `MenuOpening` con el evento `NativeMenu.Opening`; clic en el icono (`TrayIcon.Clicked`) → `OpenRequested` (en Linux no hay doble clic fiable). `ShowNotice` → `INotifier.Show` (sin notificador, nada).
- **Avisos del sistema sin bandeja**: un único método `ShowSystemNotice(title, text, onClick)` en la ventana: la bandeja si existe; si no, `App.Platform.Notifier`; si no, nada. Todos los avisos del sistema (movimiento, fallos de grabación ocultos, actualizaciones ocultas, importar oculto) pasan por él. Así, en un Linux sin bandeja, el movimiento con la ventana minimizada avisa igual. Puro y probado: `static SystemNoticeTarget Choose(bool hasTray, bool hasNotifier)` → `Tray` / `Notifier` / `None`.
- **Sin bandeja** (diseño, «Errores y casos especiales»): la X **minimiza** en lugar de cerrar (solo en macOS/Linux; en Windows sin bandeja sigue cerrando como hoy); «Salir» del menú Archivo sale. Las opciones de la bandeja siguen en el menú Archivo: casilla «{etiqueta de autoarranque}» (`TrayTexts.AutoStartLabel`) en Archivo, antes del separador de «Salir», visible solo cuando no hay bandeja y hay `AutoStart`.

- [ ] **Step 1: Pruebas** de `Choose` y, si se extrae, de la decisión de la X (`CloseAction Decide(bool exitRequested, bool systemExit, bool hasTray, OsKind os)` → `Hide` / `Minimize` / `Close`).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas y 0 advertencias (en Windows nada cambia). **Step 5: Commit** — `feat(desktop): bandeja de Avalonia y comportamiento sin bandeja`.

---

### Task 7: `PlatformServices` para Linux, textos por sistema y ping

**Files:**
- Modify: `src/Centinela.Platform/PlatformServices.cs` (rama Linux)
- Modify: `src/Centinela.Desktop/App.axaml.cs` (aviso único del archivo de contraseñas)
- Modify: `src/Centinela.Core/Settings.cs` (`bool FileSecretNoticeShown`)
- Modify: `src/Centinela.Desktop/Services/Info/ManualDocument.cs` (textos por sistema), `src/Centinela.Desktop/Services/Tray/TrayTexts.cs`
- Modify: `src/Centinela.Core/Onvif/NetworkScan.cs`, `src/Centinela.Desktop/Views/DiscoveryDialog.axaml.cs`
- Create/Modify: pruebas correspondientes (`PlatformServicesTests`, `InfoDocumentsTests`, `NetworkScanTests` si se puede sin red)

- [ ] **Rama Linux de `PlatformServices.Create`**: `Secrets` = `LinuxSecretStore(SecretServiceClient o null si no hay bus, FileSecretStore(<datos>/secret.key))` (con `CENTINELA_DATA_DIR`, solo el `FileSecretStore` de la carpeta de pruebas, como hoy); `AutoStart` = `LinuxAutoStart(DefaultDirectory, exePath)` (null con `CENTINELA_DATA_DIR`); `SingleInstance` = `UnixSingleInstance(PathFor(...))`; `Files` = `ShellFileManager`; `Notifier` = `LinuxNotifier`; `TrayAvailability` = `LinuxTrayAvailability`; `CreateTray` = null.
- [ ] **Aviso del archivo de contraseñas** (diseño): si `Secrets` es `LinuxSecretStore` con `UsesFileFallback` tras cargar o guardar las cámaras y `!FileSecretNoticeShown`, aviso informativo una vez «Las contraseñas se guardan cifradas en un archivo accesible solo por tu usuario» (título «Contraseñas») y se guarda el ajuste. Exponerlo como `bool PlatformServices.SecretsUseFileFallback` (lo calcula el almacén).
- [ ] **Manual por sistema** (pendiente «Fases 3–4»): `ManualDocument.Build(OsKind os)`; en Windows exactamente el texto actual; en Linux (y macOS, con sus textos ya aquí): «Bandeja del sistema» → en Linux «La X de la ventana la oculta en la bandeja (el icono de la barra superior) y las grabaciones continúan. Si tu escritorio no tiene bandeja, la X minimiza la ventana. «Salir» cierra Centinela del todo.»; «Arrancar con Windows» → la etiqueta del sistema («Con «Arrancar al iniciar sesión», Centinela se abre al iniciar sesión en el equipo.»); volumen → «El volumen se ajusta en los ajustes de sonido del sistema.»; carpetas → `Vídeos/Centinela`, `Imágenes/Centinela`, `Documentos/Centinela` con barras `/`; registros → `~/.local/share/centinela/logs`. macOS: barra de menús en lugar de bandeja, «Abrir al iniciar sesión», `Películas/Centinela`, `~/Library/Application Support/Centinela/logs`. Pruebas: Windows sin cambios; Linux y macOS sin la palabra «Windows».
- [ ] **Ping** (diseño §2): `NetworkScan.FindNetworksAsync` captura también `PlatformNotSupportedException`, `UnauthorizedAccessException`, `System.ComponentModel.Win32Exception` e `InvalidOperationException` del `Ping`; si el ping no está permitido en el sistema, devuelve las redes encontradas hasta entonces y lo indica (`NetworkScanResult(IReadOnlyList<string> Networks, bool PingUnavailable)`); el diálogo añade al resumen «No se pudo hacer ping en este sistema: solo se muestran las redes con cámaras ONVIF.»
- [ ] Pruebas de Core, Platform y Desktop; 0 advertencias. **Commit** — `feat: Centinela en Linux (servicios, textos y ping)`.

---

### Task 8: FFmpeg para Linux y el paquete `.deb`

**Files:**
- Create: `tools/get-ffmpeg.sh`, `tools/build-deb.sh`
- Create: `packaging/linux/centinela.desktop`, `packaging/linux/control.in`
- Modify: `Directory.Build.targets` (copiar también `ffmpeg/lib/*.so*` a `ffmpeg/` en la salida)
- Modify: `.gitattributes` (`*.sh text eol=lf`)

- [ ] **`tools/get-ffmpeg.sh`**: descarga `ffmpeg-n9.0-latest-linux64-lgpl-shared-9.0.tar.xz` de BtbN (misma versión que `get-ffmpeg.ps1`), copia `lib/*.so*` a `<repo>/ffmpeg/lib` (conservando los enlaces) y la licencia a `ffmpeg/LICENSE.txt`. `set -euo pipefail`; requiere `curl` y `tar`.
- [ ] **`tools/build-deb.sh`**: comprueba `dotnet`, `dpkg-deb` y `ffmpeg/lib` (si falta, indica ejecutar `tools/get-ffmpeg.sh`); versión de `Directory.Build.props` (o el primer argumento); `dotnet publish src/Centinela.Desktop -c Release -r linux-x64 --self-contained true -o <staging>/opt/centinela`; FFmpeg en `/opt/centinela/ffmpeg`; `/usr/bin/centinela` enlace a `/opt/centinela/Centinela`; `packaging/linux/centinela.desktop` en `/usr/share/applications/`; `centinela.png` en `/usr/share/icons/hicolor/256x256/apps/centinela.png`; `DEBIAN/control` desde `control.in` (Package `centinela`, Version, Architecture `amd64`, Maintainer «Centinela <tecxart@gmail.com>», Depends `libc6, libx11-6, libice6, libsm6, libfontconfig1, libva2, libva-drm2, libdrm2`, Description «Visor de cámaras IP» con una línea larga); permisos (directorios 755, ejecutable 755); `dpkg-deb --build --root-owner-group` → `dist/centinela_<versión>_amd64.deb`. Imprime al final cómo instalarlo: `sudo apt install ./dist/centinela_<versión>_amd64.deb`.
- [ ] **`packaging/linux/centinela.desktop`**: `Name=Centinela`, `Comment=Visor de cámaras IP`, `Exec=centinela`, `Icon=centinela`, `Categories=AudioVideo;Video;Security;`, `Terminal=false`, `StartupWMClass=Centinela`.
- [ ] `Directory.Build.targets`: además de `ffmpeg\bin\*.dll`, `ffmpeg/lib/*.so*` con `Link="ffmpeg\%(Filename)%(Extension)"`.
- [ ] Comprobación: `bash -n` de los dos scripts (sintaxis) si hay `bash`; la solución compila con 0 advertencias. **Commit** — `build: FFmpeg para Linux y paquete .deb`.

---

### Task 9: Lista de prueba en Linux y comprobación de la fase 3

**Files:**
- Create: `docs/prueba-linux-v1.4.md`
- Modify: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md`

- [ ] `docs/prueba-linux-v1.4.md`, para un usuario no técnico, con casillas: preparar la VM (Ubuntu 24.04, `sudo apt install dotnet-sdk-10.0` o el script oficial si el paquete no existe, `git clone`, `bash tools/get-ffmpeg.sh`, `bash tools/build-deb.sh`, `sudo apt install ./dist/centinela_*.deb`), abrir Centinela desde el menú de aplicaciones, y luego lo mismo que `docs/paridad-v1.4.md` adaptado a Linux, más lo propio de Linux: contraseñas en el llavero («Contraseñas y claves» / Seahorse muestra «Centinela: contraseña de la cámara …»), copiar un `cameras.json` de Windows (las cámaras aparecen con «Falta contraseña» y «Editar»), importar la copia JSON con contraseñas, bandeja en la barra superior, notificaciones de GNOME y su clic, «Arrancar al iniciar sesión» (`~/.config/autostart/centinela.desktop`), segunda instancia (abre la primera), «Detectar otras redes» con ping, VAAPI (en una VM sin GPU, «CPU» en las estadísticas es lo normal), sonido por PipeWire/PulseAudio, carpetas `~/Vídeos/Centinela` y `~/Imágenes/Centinela`.
- [ ] Pruebas de Core, Platform, Media y Desktop pasan; solución con 0 advertencias; nada de APIs nativas en Desktop; ningún archivo nuevo pasa de ~300 líneas.
- [ ] Pendientes: marcar los de la fase 3 resueltos; añadir lo que quede.
- [ ] **Commit** — `docs: prueba de Centinela en Linux`.
