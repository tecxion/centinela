# Centinela v1.4 — Fase 4: macOS — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centinela funciona en macOS (probado por el usuario en un MacBook M4 Pro) con la misma funcionalidad que en Windows y Linux. Las contraseñas van al Llavero. Hay arranque al iniciar sesión (LaunchAgent), icono en la barra de menús, notificaciones del sistema con clic, aviso de permiso de red local, VideoToolbox y una sola instancia. Un `.dmg` provisional (firma ad hoc, sin notarizar) para Apple Silicon y otro para Intel, generados por GitHub Actions.

**Architecture:** Como en las fases anteriores: interfaces en `Centinela.Core.Platform`, implementaciones de macOS en `Centinela.Platform/MacOS/` (un archivo por pieza, `[SupportedOSPlatform("macos")]`), elegidas en `PlatformServices.Create`.
- El Llavero se usa con la API C de Security.framework por P/Invoke. Las notificaciones (UserNotifications) se llaman a través del runtime de Objective-C (`libobjc`) por P/Invoke, sin paquetes nuevos.
- El almacén de contraseñas «llavero del sistema con archivo cifrado de respaldo» de Linux pasa a ser común (`KeychainSecretStore`) y lo usan Linux y macOS.
- La barra de menús es el `AvaloniaTray` que ya existe.
- FFmpeg para Mac se compila desde el código fuente (LGPL, VideoToolbox) con `tools/get-ffmpeg-mac.sh`. El `.app` y el `.dmg` los monta `tools/build-dmg.sh`. Un workflow de GitHub Actions ejecuta ambos scripts en cada push a `feat/v1.4` y deja los `.dmg` como artefactos descargables.
- Lo que se puede probar sin Mac (textos, formatos, rutas, el plist del LaunchAgent, el mapa de avisos, el almacén común) se prueba en Windows. El resto lo prueba el usuario en el Mac.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22, Security.framework (SecKeychain), UserNotifications (runtime de Objective-C), FFmpeg 9.0.2 LGPL compilado para macOS 14+, OpenAL Soft (Silk.NET.OpenAL.Soft.Native, ya trae `osx-arm64` y `osx-x64`), `codesign`, `hdiutil`, `iconutil`, GitHub Actions (`macos-15`).

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (§2 plataforma, §4 paquetes, «Errores y casos especiales», §6 fase 4). Pendientes: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md` (secciones «Fases 3–4» y «Arrastrados de la fase 2d › Fases 3–4»).

**Respuestas del usuario (26 de octubre):**
- El `.dmg` provisional lo genera GitHub Actions; el usuario lo descarga desde la pestaña Actions del repositorio (público). Esto adelanta a esta fase un workflow solo de macOS, sin firma real.
- `ci.yml`, `release.yml`, la firma con Developer ID y la notarización siguen en la fase 5.

**Decisiones del plan (rulings sobre el spec):**
- **Dentro del `.app`, todo va en `Contents/MacOS`.** Ahí van la salida de `dotnet publish` (con `libopenal.dylib`) y FFmpeg en `Contents/MacOS/ffmpeg`, donde `FFmpegLoader` ya lo busca. Es la estructura que documenta Avalonia para firmar y notarizar.
  - El spec decía `Contents/Frameworks` para FFmpeg y OpenAL. Moverlos exigiría cambiar el cargador de FFmpeg y el de Silk.NET.
  - Si en la fase 5 la notarización lo rechaza, se mueven allí.
- **Llavero con la API clásica `SecKeychain*`.** Está obsoleta desde macOS 10.10, pero sigue en macOS 15 y 26. Es la mitad de código que `SecItem*` (que exige construir diccionarios de CoreFoundation).
  - El llavero «de protección de datos» necesita un perfil de aprovisionamiento, así que no sirve.
  - Si el Llavero falla, se usa el archivo cifrado, como en Linux; el aviso de la UI ya existe.
- **«Sin conexión» en macOS no se separa del permiso denegado.** Con el permiso de red local denegado, macOS devuelve `EHOSTUNREACH`, igual que con un equipo inalcanzable. Por eso no hay un error nuevo: el consejo de «Sin conexión» en macOS añade dónde dar el permiso.
- **Corrección de los códigos de error de red por sistema.** Hoy `StreamErrorClassifier` solo conoce los `errno` de Windows, y en Linux un «conexión rechazada» sale como «Error de conexión». Se arregla aquí para Linux y macOS. Windows no cambia.

**Fuera de esta fase:** `ci.yml` (pruebas en los tres sistemas), `release.yml`, firma Developer ID, notarización y grapado (fase 5). README por sistema y versión 1.4.0 (fase 6): hasta entonces el `.dmg` lleva la versión de `Directory.Build.props`. El menú propio de la ventana (Archivo, Ver…) sigue dentro de la ventana; no pasa a la barra de menús global de macOS.

## Global Constraints

**Dependencias y alcance**
- .NET 10; Avalonia 11.3.22. **Ningún paquete NuGet nuevo.**
- Nada cambia en Windows: mismo comportamiento y mismos textos; las pruebas de Windows siguen pasando. En Linux solo cambia la clasificación de errores de red (Task 1) y el nombre del almacén de contraseñas (Task 2, mismo comportamiento).

**Textos**
- Textos visibles en español, con el mismo tono que los de Windows; los nuevos los fija este plan.

**Estructura del código**
- Un archivo, una responsabilidad; ningún archivo de más de ~300 líneas.
- Lo de macOS va solo en `Centinela.Platform/MacOS/`, con `[SupportedOSPlatform("macos")]` en lo que llama a APIs nativas. `Centinela.Desktop` no llama a APIs nativas.
- Interop con `[DllImport]`, como `Windows/TrayInterop.cs`.

**Seguridad y datos**
- Contraseñas nunca en texto claro en registros, avisos, mensajes ni archivos, salvo el Llavero o el archivo cifrado.
- Con `CENTINELA_DATA_DIR`, nada real del sistema: ni Llavero real ni LaunchAgent real.

**Pruebas**
- Las pruebas se ejecutan en Windows: solo lógica pura y archivos temporales. Ninguna prueba llama al Llavero, a Objective-C, a la red ni a las carpetas reales del usuario.

**Compilación**
- Compilar y probar Desktop o la solución con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/`. Core, Media y Platform, sin ese parámetro.
- No ejecutar la aplicación.
- Cero advertencias de compilación (incluido el analizador de plataforma).

**Git**
- `git add` con rutas explícitas; nunca los `README.sync-conflict-*.md` ni `.agents/` ni `.claude/skills/`.
- Commits con línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No hacer push (lo hace el controlador al final de la fase).

**Scripts y archivos de empaquetado**
- Los `.sh` llevan finales de línea LF (ya en `.gitattributes`) y bit de ejecución (`git update-index --chmod=+x`). Deben funcionar con el bash 3.2 de macOS: sin `mapfile`, sin `declare -A`, sin `sed -i`, y los arrays vacíos se expanden con `${a[@]+"${a[@]}"}`.
- Los archivos de `packaging/macos/` también llevan LF: añadir `packaging/macos/* text eol=lf` a `.gitattributes`.

---

### Task 1: Errores de red según el sistema y consejo de red local en macOS

**Files:**
- Modify: `src/Centinela.Media/StreamErrorClassifier.cs`
- Modify: `src/Centinela.Core/Errors/ErrorTranslator.cs`
- Modify: `src/Centinela.Desktop/Services/Info/ManualDocument.cs` (la sección «Errores frecuentes» usa el sistema del manual)
- Modify: `tests/Centinela.Media.Tests/ErrorClassificationTests.cs`, `tests/Centinela.Core.Tests/ErrorTranslatorTests.cs`, `tests/Centinela.Desktop.Tests/InfoDocumentsTests.cs` (si hace falta)

**Interfaces:**
- Produces:
  - `public static StreamErrorKind StreamErrorClassifier.Classify(int ffmpegCode, int? rtspStatus, bool wasPlaying, bool deadlineHit, OsKind os)`. La firma actual de cuatro parámetros se queda y llama a esta con `OsInfo.Current`.
  - `public static TranslatedError ErrorTranslator.Translate(StreamErrorKind kind, Brand brand, string cameraName, OsKind os)`. La firma actual de tres parámetros se queda y llama a esta con `OsInfo.Current`.

**Códigos de error que cuentan como «Sin conexión».** FFmpeg devuelve `-errno` del sistema; `EACCES` (13) sigue siendo «Contraseña incorrecta» en los tres sistemas.

| Sistema | ETIMEDOUT | ECONNREFUSED | EHOSTUNREACH | ENETUNREACH | EADDRNOTAVAIL | ECONNRESET | EHOSTDOWN |
|---|---|---|---|---|---|---|---|
| Windows (UCRT, como hoy, sin cambios) | 138 | 107 | 110 | 118 | 101 | 108 | — |
| Linux | 110 | 111 | 113 | 101 | 99 | 104 | 112 |
| macOS | 60 | 61 | 65 | 51 | 49 | 54 | 64 |

**Consejo de «Sin conexión» en macOS** (los demás sistemas, igual que hoy):

```
Comprueba que la cámara está encendida y en la misma red, y que su IP no ha cambiado (conviene reservarla en el router). Si es la primera vez, permite a Centinela el acceso a la red local en Ajustes del Sistema › Privacidad y seguridad › Red local.
```

- [ ] **Step 1: Pruebas.**
  - Clasificador:
    - Cada código de la tabla, con su sistema, da `Unreachable`.
    - En Linux, `-111` da `Unreachable` (hoy `Unknown`). En macOS, `-61` y `-65` dan `Unreachable`.
    - En macOS, `-138` da `Unknown`: el código de Windows no se confunde.
    - Las pruebas actuales siguen pasando en Windows sin tocar.
  - Traductor:
    - `Translate(Unreachable, Brand.Custom, "X", OsKind.MacOS).Advice` es exactamente el texto de arriba.
    - Con `OsKind.Windows` y `OsKind.Linux` da el consejo de hoy.
    - El resto de errores no cambia según el sistema.
  - Manual: `ManualDocument.Build(OsKind.MacOS)` contiene el consejo de macOS en «Errores frecuentes».
- [ ] **Step 2:** comprobar que fallan.
- [ ] **Step 3: Implementar.**
  - La tabla vive dentro de `StreamErrorClassifier`, como `static IReadOnlySet<int> UnreachableErrno(OsKind os)` privada (o equivalente), con un comentario de una línea por sistema.
  - `ManualDocument` pasa su `os` a `Translate`.
  - La prueba `Manual_explains_every_stream_error_after_its_heading` sigue pasando en Windows.
- [ ] **Step 4:** pruebas de Core, Media y Desktop, y solución con 0 advertencias.
- [ ] **Step 5: Commit** — `fix: errores de red de Linux y macOS, y permiso de red local en macOS`.

---

### Task 2: Almacén común con llavero y el Llavero de macOS

**Files:**
- Move/rename (`git mv`):
  - `src/Centinela.Platform/Linux/LinuxSecretStore.cs` → `src/Centinela.Platform/KeychainSecretStore.cs`
  - `src/Centinela.Platform/Linux/ISecretBackend.cs` → `src/Centinela.Platform/ISecretBackend.cs`
  - `tests/Centinela.Platform.Tests/LinuxSecretStoreTests.cs` → `tests/Centinela.Platform.Tests/KeychainSecretStoreTests.cs`
- Create: `src/Centinela.Platform/SecretReference.cs`, `src/Centinela.Platform/MacOS/MacKeychain.cs`
- Modify: `src/Centinela.Platform/Linux/SecretServiceNames.cs` (pierde `StoredValue` y `CameraOf`, que pasan a `SecretReference`), `src/Centinela.Platform/Linux/SecretServiceClient.cs` (solo si usa lo movido), `src/Centinela.Platform/PlatformServices.cs`, `tests/Centinela.Platform.Tests/PlatformServicesTests.cs` (tipos renombrados)

**Interfaces:**
- Produces:
  - `public static class SecretReference`:
    - `static string For(string prefix, Guid cameraId)` devuelve `prefix + cameraId.ToString("D")`.
    - `static Guid? CameraOf(string prefix, string storedValue)` devuelve el Id si el valor empieza por ese prefijo y le sigue un Guid "D"; si no, null.
  - `internal interface ISecretBackend`, sin cambios salvo el espacio de nombres: pasa a `Centinela.Platform`.
  - `internal sealed class KeychainSecretStore(string prefix, ISecretBackend? backend, FileSecretStore fallback) : ISecretStore` con `bool UsesFileFallback`. Se comporta exactamente como el `LinuxSecretStore` de hoy, con `prefix` en lugar de `SecretPrefix.SecretService`:
    - `Protect`: si el llavero lo guarda, devuelve `SecretReference.For(prefix, id)`. Si falla, borra lo anterior del llavero y usa el archivo (`file:`).
    - `Unprotect`: el propio prefijo va al llavero (comprobando que el Id coincide); `file:` va al archivo; cualquier otro valor da null.
    - `Forget`: borra del llavero solo los valores con su prefijo.
  - Linux: `new KeychainSecretStore(SecretPrefix.SecretService, new SecretServiceClient(bus), file)`.
  - macOS: `new KeychainSecretStore(SecretPrefix.Keychain, new MacKeychain(), file)`. Este cableado se hace en la Task 5; aquí solo la clase.
  - `PlatformServices.SecretsUseFileFallback => Secrets is KeychainSecretStore { UsesFileFallback: true }`.
  - `[SupportedOSPlatform("macos")] internal sealed class MacKeychain : ISecretBackend`: servicio `es.tecxart.centinela`, cuenta = Id de la cámara en formato "D", contraseña en UTF-8.

**`MacKeychain`.** P/Invoke a `/System/Library/Frameworks/Security.framework/Security` y `CFRelease` de `/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation`.

Funciones:
- `SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef)`.
- Una segunda declaración con `IntPtr` para la longitud y los datos de la contraseña. Pasando `IntPtr.Zero` se busca el elemento sin leer el secreto, así que no hay ventana de permiso.
- `SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef)`.
- `SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data)`.
- `SecKeychainItemDelete(IntPtr itemRef)`.
- `SecKeychainItemFreeContent(IntPtr attrList, IntPtr data)`.

Códigos: `errSecSuccess = 0` y `errSecItemNotFound = -25300`.

Métodos:
- `Store`: busca el elemento sin leer el secreto. Si existe, `ModifyAttributesAndData`; si no, `AddGenericPassword`. Devuelve true solo con `errSecSuccess`.
- `Lookup`: `FindGenericPassword` con datos; copia los bytes y los pasa a cadena (UTF-8); llama a `FreeContent` y a `CFRelease` del elemento.
- `Delete`: busca sin secreto y llama a `ItemDelete` y `CFRelease`.
- **Nunca lanza.** Cualquier excepción (`DllNotFoundException`, `EntryPointNotFoundException`…) o código distinto de 0 da `false`, `null` o nada.
- Comentario de atajo en la clase: `// shortcut: SecKeychain* (obsoleta desde 10.10, sigue en macOS 26); pasar a SecItem* si Apple la retira.`
- Comentario de una línea: macOS puede mostrar su ventana de permiso del Llavero durante la llamada (bloquea hasta que el usuario responde), por eso no hay tiempo máximo.

- [ ] **Step 1: Pruebas.** Las de `LinuxSecretStoreTests` pasan a `KeychainSecretStoreTests` con el prefijo `secret-service:`. Además:
  - Con el prefijo `keychain:`: `Protect` devuelve `keychain:<Id>`, `Unprotect` lo recupera y `Forget` lo borra.
  - Un almacén `keychain:` devuelve null para `secret-service:<Id>`, y uno `secret-service:` devuelve null para `keychain:<Id>`.
  - `SecretReference.For` y `CameraOf`: prefijo correcto, prefijo distinto y Guid mal formado.
  - Las pruebas de `SecretServiceNames` que quedan (atributos y etiqueta) siguen.
- [ ] **Step 2:** fallan (no compilan).
- [ ] **Step 3:** implementar. `MacKeychain` no tiene pruebas; no se llama en Windows.
- [ ] **Step 4:** pruebas de Platform y Desktop, y solución con 0 advertencias.
- [ ] **Step 5: Commit** — `feat(platform): contraseñas en el Llavero de macOS (almacén común con Linux)`.

---

### Task 3: Abrir al iniciar sesión en macOS (LaunchAgent)

**Files:**
- Create: `src/Centinela.Platform/MacOS/LaunchAgent.cs` (puro), `src/Centinela.Platform/MacOS/MacAutoStart.cs`
- Create: `tests/Centinela.Platform.Tests/MacAutoStartTests.cs`

**Interfaces:**
- Produces:
  - `public static class LaunchAgent`:
    - `const string Label = "es.tecxart.centinela"`.
    - `static string For(string exePath)` devuelve el contenido exacto de abajo, con la ruta escapada para XML.
    - `static string? ProgramOf(string plistText)` devuelve el primer `<string>` de `ProgramArguments`, o null si no se puede leer.
    - `static bool IsDisabled(string plistText)` es true si el diccionario tiene `<key>Disabled</key><true/>`.
  - `public sealed class MacAutoStart(string agentsDir, string exePath) : IAutoStart`, con `static string DefaultDirectory(string home)`, que devuelve `<home>/Library/LaunchAgents`.

Archivo `<agentsDir>/es.tecxart.centinela.plist`, con tabuladores para sangrar, como lo escribe macOS, y LF:

```
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>Label</key>
	<string>es.tecxart.centinela</string>
	<key>ProgramArguments</key>
	<array>
		<string>{exePath escapado}</string>
		<string>--tray</string>
	</array>
	<key>RunAtLoad</key>
	<true/>
</dict>
</plist>
```

- **Lectura.** `XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null })` y `XDocument.Load(reader)`. Con la configuración por defecto, el `DOCTYPE` lanza una excepción. Cualquier error de lectura da null o false, nunca lanza.
- **Escritura.** `SecurityElement.Escape` (o `XText`) para `&`, `<` y `>`.
- **Métodos de `MacAutoStart`.** Mismo patrón que `LinuxAutoStart`:
  - `IsEnabled`: el archivo existe, no está desactivado y su programa es `exePath`.
  - `Enable`: escritura atómica (temporal y `File.Move` con `overwrite`), UTF-8 sin BOM.
  - `Disable`: borra el archivo.
  - `Migrate`: reescribe un archivo activo que apunta a otro programa; devuelve si cambió algo. Uno desactivado se deja como está.
  - Errores de E/S al leer, como en Linux: devuelven null.
- Comentario de una línea: el archivo se lee al iniciar sesión. `Enable` no arranca nada ahora y `Disable` no para la copia en marcha.

- [ ] **Step 1: Pruebas** en una carpeta temporal:
  - El contenido exacto.
  - Una ruta con `&` y espacios se escapa y `ProgramOf` la devuelve tal cual.
  - `Enable` hace que `IsEnabled` sea true; `Disable`, que sea false.
  - Un plist con `Disabled` da `IsEnabled` false y `Migrate` no lo toca.
  - `Migrate` corrige otra ruta y devuelve false si ya apunta aquí.
  - Un archivo que no es XML da `IsEnabled` false sin lanzar.
  - `DefaultDirectory("/Users/ana")` da `/Users/ana/Library/LaunchAgents`.
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas de Platform y 0 advertencias.
- [ ] **Step 5: Commit** — `feat(platform): abrir al iniciar sesión en macOS (LaunchAgent)`.

---

### Task 4: Notificaciones de macOS

**Files:**
- Create: `src/Centinela.Platform/MacOS/ObjC.cs` (runtime de Objective-C: clases, selectores, `objc_msgSend`, `NSString`, bloques)
- Create: `src/Centinela.Platform/MacOS/MacNotifier.cs`
- Create: `src/Centinela.Platform/MacOS/NoticeActions.cs` (puro)
- Modify: `src/Centinela.Platform/Centinela.Platform.csproj` (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`)
- Create: `tests/Centinela.Platform.Tests/NoticeActionsTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed class NoticeActions(int capacity = 50)`, segura entre hilos (`lock`):
    - `string Add(Action onClick)` devuelve un identificador nuevo (`Guid.NewGuid().ToString("N")`).
    - `Action? Take(string id)` devuelve la acción una sola vez y la quita.
    - Al pasar de `capacity` se descarta la más antigua: las notificaciones que nadie pulsa no se acumulan.
  - `[SupportedOSPlatform("macos")] public sealed class MacNotifier : INotifier` con `public static MacNotifier? TryCreate(SynchronizationContext ui)`.

**`TryCreate`.** Devuelve null, sin lanzar nunca, si:
- no se pueden cargar `libobjc` o `UserNotifications`;
- `[[NSBundle mainBundle] bundleIdentifier]` es nil (Centinela no se ejecuta desde un `.app`, por ejemplo con `dotnet run`: sin paquete, `UNUserNotificationCenter` lanza una excepción de Objective-C que tumba el proceso);
- no existe la clase `UNUserNotificationCenter`.

Si todo está, en orden:
1. Carga `/System/Library/Frameworks/UserNotifications.framework/UserNotifications` con `NativeLibrary.Load`.
2. Crea, una sola vez por proceso, la clase delegada `CentinelaNotificationDelegate`: `objc_allocateClassPair(NSObject)`, `class_addMethod` con los dos métodos de abajo y su tipo `"v@:@@@?"`, `class_addProtocol` con `UNUserNotificationCenterDelegate` si `objc_getProtocol` la encuentra, y `objc_registerClassPair`. Si `objc_getClass` ya la encuentra, la reutiliza.
3. Crea una instancia (`alloc` / `init`), que nunca se libera, y la pone como `delegate` del centro (`[UNUserNotificationCenter currentNotificationCenter]`).
4. Pide permiso con `requestAuthorizationWithOptions:completionHandler:`, con la opción Alert (`1 << 2`). El `completionHandler` no admite nil: es un bloque global.

**Métodos del delegado** (`[UnmanagedCallersOnly]`, firma `(IntPtr self, IntPtr cmd, IntPtr center, IntPtr arg, IntPtr completionHandler)`; todo dentro de `try/catch`, porque una excepción gestionada ahí tumba el proceso):
- `userNotificationCenter:willPresentNotification:withCompletionHandler:` llama al bloque con `Banner | List` (`(1 << 4) | (1 << 3)`), para que el aviso salga también con Centinela en primer plano. Sin sonido: Centinela pone el suyo.
- `userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:` lee el identificador (`response.notification.request.identifier`, con `UTF8String`) y saca su acción de `NoticeActions`. Si hay una, la publica en `ui` (`ui.Post(_ => action(), null)`). Después llama siempre al bloque, sin argumentos.

**`Show(title, text, onClick)`** (hilo de la interfaz), dentro de `objc_autoreleasePoolPush` / `objc_autoreleasePoolPop`:
- Crea `UNMutableNotificationContent` (`alloc` / `init`) con `setTitle:` y `setBody:`. Los `NSString` se crean con `initWithBytes:length:encoding:` (UTF-8 = 4) y se liberan con `release` después de asignarlos.
- El identificador sale de `NoticeActions.Add(onClick ?? (() => { }))`.
- Llama a `[UNNotificationRequest requestWithIdentifier:content:trigger:nil]` y a `[center addNotificationRequest:request withCompletionHandler:nil]`, y después `release` del contenido.
- Si falla algo gestionado, no hace nada (el aviso no sale; el marco rojo y el registro siguen).

**`Dispose`.** Quita el delegado (`setDelegate:nil`) y deja de publicar clics. La clase y el bloque global se quedan: viven lo que el proceso.

**Bloques (en `ObjC.cs`).**
- Recibido: el puntero de la función está en el desplazamiento 16 del bloque. Se llama como `((delegate* unmanaged<IntPtr, nuint, void>)invoke)(block, options)` o `((delegate* unmanaged<IntPtr, void>)invoke)(block)`.
- Global propio, creado una vez en memoria no gestionada (`NativeMemory.Alloc`, nunca se libera):
  - `isa` es `NativeLibrary.GetExport(libSystem, "_NSConcreteGlobalBlock")` (con `/usr/lib/libSystem.B.dylib`).
  - `flags = 1 << 28` (BLOCK_IS_GLOBAL) y `reserved = 0`.
  - `invoke` es el puntero de una función `[UnmanagedCallersOnly] static void (IntPtr block, byte granted, IntPtr error)` que no hace nada.
  - `descriptor` apunta a `{ nuint reserved = 0; nuint size = 32; }`.

**`objc_msgSend`.** P/Invoke a `/usr/lib/libobjc.A.dylib` con `EntryPoint = "objc_msgSend"`, una declaración por firma usada. Por ejemplo:
- `IntPtr (IntPtr, IntPtr)`
- `IntPtr (IntPtr, IntPtr, IntPtr)`
- `IntPtr (IntPtr, IntPtr, IntPtr, IntPtr, IntPtr)`
- `IntPtr (IntPtr, IntPtr, byte*, nuint, nuint)`
- `void (IntPtr, IntPtr, nuint, IntPtr)`
- `void (IntPtr, IntPtr, IntPtr, IntPtr)`

Funciones del runtime: `objc_getClass`, `sel_registerName`, `objc_getProtocol`, `objc_allocateClassPair`, `class_addMethod`, `class_addProtocol`, `objc_registerClassPair`, `objc_autoreleasePoolPush` y `objc_autoreleasePoolPop`.

- [ ] **Step 1: Pruebas de `NoticeActions`:**
  - `Take` devuelve la acción una vez y después null.
  - Un id desconocido da null.
  - Con capacidad 2, al añadir 3 la primera ya no está y las otras dos sí.
  - Los identificadores no se repiten.
- [ ] **Step 2:** fallan. **Step 3:** implementar. `MacNotifier` y `ObjC` no tienen pruebas; nunca se llaman en Windows.
- [ ] **Step 4:** pruebas de Platform y 0 advertencias.
- [ ] **Step 5: Commit** — `feat(platform): notificaciones de macOS con clic`.

---

### Task 5: macOS en los servicios de plataforma y en la interfaz

**Files:**
- Modify: `src/Centinela.Platform/PlatformServices.cs`
- Modify: `src/Centinela.Desktop/App.axaml.cs`, `src/Centinela.Desktop/Views/MainWindow.Help.cs`
- Modify: `src/Centinela.Desktop/Services/Info/ManualSystemTexts.cs` (textos de macOS)
- Modify: `tests/Centinela.Platform.Tests/PlatformServicesTests.cs`, `tests/Centinela.Desktop.Tests/InfoDocumentsTests.cs` (si cambian textos comprobados)

**Interfaces:**
- Consumes:
  - `KeychainSecretStore`, `MacKeychain` (Task 2).
  - `MacAutoStart`, `MacAutoStart.DefaultDirectory` (Task 3).
  - `MacNotifier.TryCreate` (Task 4).
  - `UnixSingleInstance` y `UnixSingleInstance.PathFor` (fase 3).
- Produces: `[SupportedOSPlatform("macos")] internal static PlatformServices PlatformServices.ForMacOS(string exePath, bool testRun, string dataDir, string home, SynchronizationContext ui)`.

**`PlatformServices`:**
- `Create` llama a `ForMacOS(exePath, testRun, AppPaths.DataDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ui)` cuando `OperatingSystem.IsMacOS()`.
- Se borran la rama «test run en otro sistema» y la clase `NoSingleInstance`. Los demás sistemas lanzan `PlatformNotSupportedException` con el mensaje de hoy.
- `ForMacOS`:
  - `Secrets = testRun ? file : new KeychainSecretStore(SecretPrefix.Keychain, new MacKeychain(), file)`.
  - `AutoStart = testRun ? null : new MacAutoStart(MacAutoStart.DefaultDirectory(home), exePath)`.
  - `SingleInstance = new UnixSingleInstance(UnixSingleInstance.PathFor(null, dataDir, AppPaths.InstanceSuffix))`. macOS no tiene `XDG_RUNTIME_DIR`: el socket va en la carpeta de datos.
  - `Files = new ShellFileManager()`.
  - `Notifier = MacNotifier.TryCreate(ui)`. Va también con datos de prueba, como en Linux: no toca datos.
  - `TrayAvailability` es una clase privada anidada que siempre devuelve true (macOS siempre tiene barra de menús). Con ella, `MainWindow` crea el `AvaloniaTray` sin cambios.
- Actualizar el comentario de la clase (hoy dice «macOS arrives in phase 4»).

**`App` (Desktop):**
- **Dock.** En `Start`, tras crear la ventana, si `Current?.TryGetFeature<IActivatableLifetime>()` existe, se suscribe `Activated`. Con `e.Kind == ActivationKind.Reopen` (clic en el icono del Dock), llama a `UiThread.Post(window.ShowFromTray)`. Solo macOS lanza `Reopen`; no hace falta comprobar el sistema.
- **Menú de la aplicación.** Solo con `OsInfo.Current == OsKind.MacOS`, `NativeMenu.SetMenu(this, ...)` con un único elemento: «Acerca de Centinela». Al pulsarlo llama a `_window?.ShowAbout()`. Así desaparece el «About Avalonia» por defecto; las opciones estándar de macOS (Ocultar, Salir ⌘Q) las añade Avalonia.
- `MainWindow.Help.cs`: `internal void ShowAbout()`. Si la ventana está oculta en la barra de menús, antes `ShowFromTray()`; después `_ = new AboutWindow().ShowDialog(this)`. `About_Click` llama a `ShowAbout()`.

**Textos del manual en macOS** (`ManualSystemTexts.MacOS`; los de Windows y Linux no cambian):
- `Tray`:
  ```
  El botón rojo de la ventana la oculta en la barra de menús (el icono de arriba a la derecha) y las grabaciones continúan. Para volver a abrirla, usa ese icono o haz clic en Centinela en el Dock. «Salir» (o ⌘Q) cierra Centinela del todo.
  ```
- `MotionNotice`, sin cambios.

- [ ] **Step 1: Pruebas.**
  - `PlatformServices.ForMacOS(..., testRun: false, ...)` se construye en Windows sin tocar nada real, como la prueba de `ForLinux`. Comprueba:
    - el `KeychainSecretStore` con prefijo `keychain:`;
    - `MacAutoStart`;
    - `UnixSingleInstance`;
    - `Notifier` null (en Windows `TryCreate` no encuentra `libobjc`);
    - `TrayAvailability.IsAvailable` true.
  - Con `testRun: true`: `FileSecretStore` y sin `AutoStart`.
  - El texto nuevo de la barra de menús en el manual de macOS.
  - Las pruebas de manual existentes siguen pasando (sin «bandeja» en macOS).
- [ ] **Step 2:** fallan. **Step 3:** implementar. **Step 4:** pruebas de Platform y Desktop, y solución con 0 advertencias.
- [ ] **Step 5: Commit** — `feat: macOS en los servicios de plataforma, el Dock y el menú de la aplicación`.

---

### Task 6: FFmpeg para Mac, `.app` y `.dmg`, GitHub Actions y guía de prueba

**Files:**
- Create: `tools/get-ffmpeg-mac.sh`, `tools/build-dmg.sh` (LF y bit de ejecución)
- Create: `packaging/macos/Info.plist`, `packaging/macos/Centinela.entitlements`
- Create: `.github/workflows/macos-dmg.yml`
- Create: `docs/prueba-macos-v1.4.md`
- Modify: `Directory.Build.targets` (FFmpeg de macOS), `.gitattributes` (`packaging/macos/* text eol=lf`), `.gitignore` (si `dist/` o `ffmpeg/` no están ya ignorados), `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md`, `docs/prueba-video-v1.4.md` (la orden de macOS para `CENTINELA_CPU_VIDEO=1`)

**`tools/get-ffmpeg-mac.sh arm64|x64`.** Compila FFmpeg en `<repo>/ffmpeg/osx-<arm64|x64>/lib` (en un Mac con las herramientas de línea de órdenes de Xcode; x64 necesita además `nasm`).
- **Comprobaciones.**
  - Argumento: si no es válido, sale con `Uso: tools/get-ffmpeg-mac.sh arm64|x64`.
  - Herramientas: `git`, `make` y `clang` (y `nasm` para x64). Si falta una, `Falta '<herramienta>'. Instálala y vuelve a intentarlo.` (para nasm: `brew install nasm`).
- **Código fuente.** `git clone --depth 1 --branch n9.0.2 https://github.com/FFmpeg/FFmpeg.git` en una carpeta temporal, que se borra con `trap`. `git rev-parse HEAD` tiene que ser `946fcce07b6dcd0331c8cc609192aeff5e1924f8`; si no, sale con `El código de FFmpeg descargado no coincide con n9.0.2.`.
- **`./configure`.** Las opciones:
  - `--prefix=<tmp>/out --enable-shared --disable-static --disable-programs --disable-doc --disable-debug`
  - `--disable-autodetect --enable-pthreads --enable-videotoolbox --enable-audiotoolbox --enable-zlib --enable-bzlib --enable-iconv`. Sin `--enable-pthreads`, `--disable-autodetect` quita los hilos; y el autodetect metería librerías de Homebrew que no existen en el Mac del usuario.
  - `--install-name-dir=@loader_path`
  - `--extra-cflags="-arch <cpu> -mmacosx-version-min=14.0" --extra-ldflags="-arch <cpu> -mmacosx-version-min=14.0"`, con `<cpu>` `arm64` o `x86_64`.
  - Si `<cpu>` no es `uname -m`, además `--enable-cross-compile --arch=<cpu> --target-os=darwin`.
- **Compilación.** `make -j"$(sysctl -n hw.ncpu)"` y `make install`.
- **Comprobación de dependencias.** Con `otool -L`, cada `lib*.dylib` instalado (no los enlaces) solo puede depender de `@loader_path/…`, `/usr/lib/…` o `/System/…`. Si no, sale con `Dependencia no permitida en <lib>: <ruta>`.
- **Copia.** Borra y crea `ffmpeg/osx-<arch>/lib` y copia con `cp -L` solo los nombres `lib<nombre>.<N>.dylib` (regex `^lib[a-z0-9_]+\.[0-9]+\.dylib$`). Copia `COPYING.LGPLv2.1` como `ffmpeg/osx-<arch>/LICENSE.txt`.

**`Directory.Build.targets`.** Para un RID `osx-*` (o sin RID en macOS: `$(NETCoreSdkRuntimeIdentifier)`), copia `ffmpeg/<rid>/lib/*.dylib` como `ffmpeg/%(Filename)%(Extension)`, igual que los `.so` de Linux. Comentario de una línea, como los otros dos bloques.

**`packaging/macos/Info.plist`** (con `@VERSION@`). Plist XML con estas claves:

| Clave | Valor |
|---|---|
| `CFBundleName`, `CFBundleDisplayName` | `Centinela` |
| `CFBundleIdentifier` | `es.tecxart.centinela` |
| `CFBundleExecutable` | `Centinela` |
| `CFBundleIconFile` | `centinela` |
| `CFBundlePackageType` | `APPL` |
| `CFBundleShortVersionString`, `CFBundleVersion` | `@VERSION@` |
| `CFBundleInfoDictionaryVersion` | `6.0` |
| `LSMinimumSystemVersion` | `14.0` |
| `NSHighResolutionCapable` | `true` |
| `NSLocalNetworkUsageDescription` | `Centinela se conecta a las cámaras de tu red local y las busca en ella.` |
| `NSDocumentsFolderUsageDescription` | `Centinela guarda en Documentos la copia automática de la lista de cámaras.` |

**`packaging/macos/Centinela.entitlements`.** Plist con `com.apple.security.cs.allow-jit`, `com.apple.security.cs.allow-unsigned-executable-memory` y `com.apple.security.cs.disable-library-validation`, los tres a `true`.

**`tools/build-dmg.sh arm64|x64 [versión]`.** Genera `dist/Centinela-<versión>-macos-<arm64|intel>.dmg`; por defecto la versión es la `<Version>` de `Directory.Build.props`, leída como en `build-deb.sh`.
- **Comprobaciones.**
  - Herramientas: `dotnet`, `codesign`, `hdiutil`, `sips` e `iconutil`.
  - FFmpeg: `ffmpeg/osx-<arch>/lib/libavcodec.*.dylib` tiene que existir. Si no: `Faltan las librerías de FFmpeg en ffmpeg/osx-<arch>/lib. Ejecuta antes: tools/get-ffmpeg-mac.sh <arch>`.
- **Montaje.** Carpeta de trabajo `dist/macos-<arch>` (borrada al empezar y con `trap`), con `dmg/Centinela.app` y el enlace `dmg/Applications -> /Applications`.
  - `dotnet publish src/Centinela.Desktop -c Release -r osx-<arch> --self-contained true -o <app>/Contents/MacOS`.
  - FFmpeg copiado explícitamente a `<app>/Contents/MacOS/ffmpeg/` (más `LICENSE.txt`), como en `build-deb.sh`: el paquete no depende de `Directory.Build.targets`.
  - `Info.plist` con `sed "s/@VERSION@/$version/g"` (sin `-i`).
- **Icono.** Un `.iconset` desde `src/Centinela.Desktop/Assets/centinela.png` con `sips -z`, en 16, 32, 128, 256 y 512 más sus `@2x`, y `iconutil -c icns` a `Contents/Resources/centinela.icns`. El PNG mide 300 px: los tamaños mayores se amplían, lo que se acepta.
- **Firma.** `identity="${CODESIGN_IDENTITY:--}"`: ad hoc por defecto; la fase 5 pasa el Developer ID. `--timestamp` solo si la identidad no es `-`.
  - Primero cada archivo de `Contents/MacOS` (con `find … -type f`) y después el `.app`. Todos con `codesign --force --options runtime --entitlements packaging/macos/Centinela.entitlements --sign "$identity"`.
  - Después `codesign --verify --strict "$app"`.
- **`.dmg`.** `hdiutil create -volname Centinela -srcfolder <dmg> -ov -format UDZO <dmg final>`. Con una identidad real, se firma también el `.dmg`. Al final escribe la ruta.

**`.github/workflows/macos-dmg.yml`** (nombre: `macOS (.dmg de prueba)`):
- **Disparo.** `on: push` a `feat/v1.4` con `paths-ignore: ['docs/**', '**/*.md']`, y `workflow_dispatch`.
- **Trabajo.** `permissions: contents: read`. Un trabajo con `strategy.matrix.arch: [arm64, x64]` y `fail-fast: false`, en `runs-on: macos-15`.
- **Pasos:**
  1. `actions/checkout@v6`
  2. `actions/setup-dotnet@v5` con `dotnet-version: '10.0.x'`
  3. `actions/cache@v5` de `ffmpeg/osx-${{ matrix.arch }}`, con clave `ffmpeg-osx-${{ matrix.arch }}-${{ hashFiles('tools/get-ffmpeg-mac.sh') }}`
  4. Si es x64 y no hay caché: `brew install nasm`
  5. Si no hay caché: `bash tools/get-ffmpeg-mac.sh ${{ matrix.arch }}`
  6. `bash tools/build-dmg.sh ${{ matrix.arch }}`
  7. `actions/upload-artifact@v6` con `name: Centinela-macos-${{ matrix.arch }}`, `path: dist/*.dmg` y `if-no-files-found: error`

**`docs/prueba-macos-v1.4.md`.** Guía del usuario en español, mismo estilo que `docs/prueba-linux-v1.4.md`. Secciones:
- **Descargar.** En GitHub, Actions › «macOS (.dmg de prueba)», la última ejecución verde de `feat/v1.4`, el artefacto `Centinela-macos-arm64` (hay que haber iniciado sesión en GitHub). El `.zip` lleva el `.dmg` dentro.
- **Instalar y primer arranque.**
  - Arrastrar a Aplicaciones.
  - macOS lo bloquea por no estar notarizado: Ajustes del Sistema › Privacidad y seguridad › «Abrir igualmente». Alternativa: `xattr -dr com.apple.quarantine /Applications/Centinela.app`.
  - Permisos que pide la primera vez: red local, notificaciones y, al hacer la copia automática, Documentos.
  - Ventana del Llavero tras cada `.dmg` nuevo («Permitir siempre»). Es por la firma ad hoc; con la firma de la fase 5 desaparece.
- **Comprobar:**
  - las cámaras, su vídeo y «GPU» y «pintado GPU» en las estadísticas (VideoToolbox y OpenGL);
  - el sonido de la cámara y los sonidos de aviso (OpenAL);
  - el icono de la barra de menús, con su menú y «Abrir»;
  - el botón rojo oculta la ventana y el clic en el Dock la vuelve a abrir;
  - ⌘Q y «Acerca de Centinela» en el menú de la aplicación;
  - «Abrir al iniciar sesión»: el archivo `~/Library/LaunchAgents/es.tecxart.centinela.plist`, cerrar sesión y volver a entrar; macOS avisa «Ítem añadido en segundo plano»;
  - la instancia única (abrir dos veces, también con `open -n`);
  - el aviso de movimiento con la ventana minimizada u oculta, y que el clic abre la ventana;
  - grabaciones en Películas/Centinela y capturas en Imágenes/Centinela; «Mostrar en Finder»;
  - «Buscar en red» (multidifusión) y «Detectar otras redes» (ping);
  - denegar la red local en Ajustes muestra en la cámara el consejo de Ajustes;
  - exportar e importar la copia;
  - actualizaciones: el archivo de este equipo es `…-macos-arm64.dmg`;
  - comparar con `CENTINELA_CPU_VIDEO=1 /Applications/Centinela.app/Contents/MacOS/Centinela`.
- **Opcional: Intel en Rosetta.** El artefacto `Centinela-macos-x64` se puede probar en el mismo Mac (Rosetta 2); será más lento.
- **Qué contar si algo falla.** Qué hiciste, qué esperabas y qué salió. El registro de errores (Registro, en la barra inferior). Y, si se cierra sola, el informe de Consola › Informes de fallos.

**Pendientes** (`2026-09-29-v1.4-pendientes.md`):
- Nueva sección «Arrastrados de la fase 4 (macOS)» con lo que queda por comprobar en el Mac. Incluye: la ventana del Llavero con firma ad hoc; la longitud máxima (104) de la ruta del socket con carpetas de datos largas; y la ausencia de pruebas de macOS en CI hasta la fase 5.
- Marcar ✔ lo de macOS de «Fases 3–4» (OpenAL queda por oír en el Mac, el `ITray` de macOS y los textos del manual).

**`docs/prueba-video-v1.4.md`.** La orden de macOS para `CENTINELA_CPU_VIDEO=1` (hoy dice que llega en la fase 4).

- [ ] **Step 1:** scripts, plist, entitlements, targets y workflow. Comprobar en Windows lo que se pueda:
  - `bash -n` de los dos scripts (sintaxis, con el bash de Git);
  - que el YAML se lee (por ejemplo con `python -c "import yaml…"` si está, o revisándolo a mano);
  - `dotnet build` de la solución con 0 advertencias (los targets nuevos no cambian nada en Windows).
- [ ] **Step 2:** la guía de prueba, las pendientes y la nota de la guía de vídeo.
- [ ] **Step 3:** `git update-index --chmod=+x tools/get-ffmpeg-mac.sh tools/build-dmg.sh` y comprobar con `git ls-files -s` que salen con `100755`.
- [ ] **Step 4: Commit** — `feat: .dmg de prueba para macOS con GitHub Actions (FFmpeg con VideoToolbox)`.
