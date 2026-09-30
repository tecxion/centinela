# Centinela v1.4 — Fase 2c: menús y diálogos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El Centinela Avalonia gana los menús y diálogos de la WPF: Ayuda (Manual, Licencia, Soporte, Acerca de), Archivo (Importar/Exportar JSON, carpeta y copia automática), «🔍 Buscar en red», «Calidad de la cámara…» y Ver › «Opciones de avisos…», más los pendientes de la 2b para esta fase y la conservación de contraseñas ilegibles.

**Architecture:** Como en 2a/2b: la lógica de cada diálogo vive en clases puras y probadas (`Services/<área>/`, `ViewModels/`); las vistas (`Views/`) solo pintan, recogen clics y llaman al sistema a través de Avalonia (selectores de archivo `StorageProvider`, portapapeles `Clipboard`, enlaces `Launcher`) o de `App.Platform`. `MainWindow` crece solo con archivos parciales por tema (`MainWindow.Help.cs`, `MainWindow.Backup.cs`, `MainWindow.Discovery.cs`, `MainWindow.Options.cs`).

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22 (sin paquetes nuevos: nada de `Avalonia.Controls.DataGrid`), CommunityToolkit.Mvvm 8.4.2, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (§3 interfaz, §5 datos: contraseñas y copias entre sistemas). Pendientes: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md` (secciones «Fase 2», «2c»).

**Referencia de comportamiento:** la WPF, que no se toca: `src/Centinela.App/MainWindow.Menu.cs` (Archivo y Ayuda, copia automática), `InfoDocuments.cs`, `InfoWindow.xaml(.cs)`, `AboutWindow.xaml(.cs)`, `AppInfo.cs`, `ExportDialog.xaml(.cs)`, `PassphraseDialog.xaml(.cs)`, `DiscoveryDialog.xaml(.cs)` y `MainWindow.xaml.cs` (`Discover_Click`), `CameraQualityWindow.xaml(.cs)` y `MainWindow.View.cs` (`EncoderInfoRequested`), `NotificationOptionsWindow.xaml(.cs)` y `MainWindow.Motion.cs` (`NotificationOptions_Click`).

**Fuera de esta fase (2d):** Ayuda › «Buscar actualizaciones…» y `UpdateWindow`, registros de errores y de movimiento, bandeja (incluido «importar con la ventana oculta»), avisos, detección de movimiento (`motion.Apply` tras importar o cambiar opciones), centro de errores (`errors.Forget`). **Fases 3–4:** adaptar los textos del Manual a macOS y Linux (bandeja/barra de menús, carpetas, mezclador de volumen, «Arrancar con…»).

## Global Constraints

- .NET 10; Avalonia 11.3.22; CommunityToolkit.Mvvm 8.4.2. Sin paquetes NuGet nuevos.
- `src/Centinela.App` (WPF) **no se modifica** (los cambios de Core le llegan, y deben mantenerla compilando).
- Textos visibles idénticos a los de la WPF (títulos, etiquetas, botones, tooltips, mensajes), salvo donde este plan diga otra cosa.
- Un archivo, una responsabilidad; ningún archivo de más de ~300 líneas.
- Las vistas sin lógica de negocio; nada de APIs de Windows en `Centinela.Desktop` (selectores, portapapeles y enlaces con las APIs de Avalonia; carpetas con `App.Platform.Files`).
- Contraseñas nunca en texto claro en registros, informes, portapapeles ni mensajes.
- Pruebas solo de código (lógica pura, archivos en carpetas temporales). Ninguna prueba toca `%APPDATA%\Centinela`, Documentos, Vídeos, Imágenes ni la red real.
- Compilar Desktop/App/solución siempre con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/`; las pruebas de Desktop también con ese `BaseOutputPath` (el usuario puede tener abierto el Centinela de `src/Centinela.Desktop/bin`). No ejecutar la aplicación ni tocar ningún Centinela en marcha.
- `git add` con rutas explícitas (nunca los `README.sync-conflict-*.md`); commits con línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Cero advertencias de compilación.

---

### Task 1: Pendientes pequeños: `ExplorerFileManager` con raíz de unidad; prueba completa de `EditedCamera.Merge`

**Files:**
- Modify: `src/Centinela.Platform/Windows/ExplorerFileManager.cs`
- Modify: `tests/Centinela.Platform.Tests/FileManagerTests.cs`
- Modify: `tests/Centinela.Desktop.Tests/EditedCameraTests.cs`

**Interfaces:** sin cambios públicos.

- [ ] **Step 1: Pruebas** — añadir a `FileManagerTests`:

```csharp
    [Fact]
    public void Explorer_never_ends_a_quoted_folder_with_a_backslash()
    {
        // "C:\" would reach explorer as C:" (the backslash escapes the quote).
        Assert.Equal(@"D:\", ExplorerFileManager.CommandForFolder(@"D:\").Arguments);
        Assert.Equal("\"C:\\Vídeos\\Centinela\"", ExplorerFileManager.CommandForFolder(@"C:\Vídeos\Centinela\").Arguments);
    }
```

y a `EditedCameraTests` (con `using System.Reflection;`):

```csharp
    // Every settable Camera property must be listed on one side; a new property fails here until it is decided.
    static readonly string[] FromDialog =
    [
        nameof(Camera.Id), nameof(Camera.Name), nameof(Camera.Brand), nameof(Camera.Host), nameof(Camera.Port),
        nameof(Camera.User), nameof(Camera.Password), nameof(Camera.MainUrlOverride), nameof(Camera.SubUrlOverride),
        nameof(Camera.UseUdp),
    ];

    static readonly string[] FromStored =
    [
        nameof(Camera.Order), nameof(Camera.LowQualityWhenBig), nameof(Camera.Smoothing), nameof(Camera.MotionEnabled),
        nameof(Camera.MotionSensitivity), nameof(Camera.MotionCooldownSeconds), nameof(Camera.ConnectionAlerts),
        nameof(Camera.MotionAlerts), nameof(Camera.OriginalEncoders),
    ];

    static IEnumerable<PropertyInfo> Settable =>
        typeof(Camera).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite);

    /// <summary>A camera whose every settable property has a value that depends on <paramref name="seed"/> (1 or 2).</summary>
    static Camera Filled(int seed)
    {
        var camera = new Camera();
        foreach (var p in Settable)
        {
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object value = type == typeof(string) ? $"{p.Name}-{seed}"
                : type == typeof(int) ? 100 + seed
                : type == typeof(bool) ? seed == 1
                : type == typeof(Guid) ? new Guid(seed, 0, 0, new byte[8])
                : type.IsEnum ? Enum.GetValues(type).GetValue(seed % Enum.GetValues(type).Length)!
                : throw new InvalidOperationException($"Tipo sin valor de prueba: {type}");
            p.SetValue(camera, value);
        }
        return camera;
    }

    [Fact]
    public void Merge_takes_every_property_from_the_dialog_or_from_the_stored_camera()
    {
        Assert.Equal(FromDialog.Concat(FromStored).Order(), Settable.Select(p => p.Name).Order());
        Camera edited = Filled(1), current = Filled(2);
        var merged = EditedCamera.Merge(edited, current);
        foreach (var p in Settable)
            Assert.Equal(p.GetValue(FromDialog.Contains(p.Name) ? edited : current), p.GetValue(merged));
    }
```

- [ ] **Step 2: Ejecutar** — la de Explorer falla; la de `Merge` pasa (si no pasa, arreglar `EditedCamera.Merge`, no la prueba).

- [ ] **Step 3: Implementar** — en `ExplorerFileManager`:

```csharp
    internal static ProcessStartInfo CommandForFolder(string folder) => new("explorer.exe", Quote(folder));

    internal static ProcessStartInfo CommandForFile(string file) => new("explorer.exe", $"/select,{Quote(file)}");

    /// <summary>
    /// Quoted path without a trailing backslash (it would escape the closing quote). A drive root keeps its
    /// backslash and goes unquoted: it has no spaces.
    /// </summary>
    static string Quote(string path)
    {
        var trimmed = path.TrimEnd('\\');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : $"\"{trimmed}\"";
    }
```

- [ ] **Step 4:** pruebas de Platform y Desktop pasan; 0 advertencias. **Step 5: Commit** — `fix: raíz de unidad en el explorador y prueba completa de Merge`.

---

### Task 2: Contraseñas ilegibles en este sistema (Core)

Diseño §5 y pendiente «Fase 2»: si `cameras.json` trae una contraseña que este sistema no puede leer (DPAPI de otro equipo, una referencia `keychain:` en Windows…), hoy se carga como `""` y el siguiente guardado la **borra**. Debe conservarse tal cual hasta que el usuario escriba otra.

**Files:**
- Modify: `src/Centinela.Core/Camera.cs`, `src/Centinela.Core/CameraStore.cs`
- Modify: `src/Centinela.Desktop/ViewModels/CameraForm.cs`, `src/Centinela.Desktop/Views/AddCameraDialog.axaml.cs`
- Modify: `tests/Centinela.Core.Tests/CameraStoreTests.cs`, `tests/Centinela.Core.Tests/CameraDuplicateTests.cs`, `tests/Centinela.Desktop.Tests/CameraFormTests.cs`, `tests/Centinela.Desktop.Tests/EditedCameraTests.cs`

**Interfaces:**
- Produces:
  - `Camera.UnreadablePassword` (`string?`): el valor guardado tal cual cuando este sistema no pudo leerlo; `null` si se leyó (o no había). `Camera.PasswordMissing => UnreadablePassword is not null` (solo lectura).
  - `CameraForm.PasswordNote` (`const string`): «La contraseña guardada no se puede leer en este equipo: escríbela de nuevo.»

- [ ] **Step 1: Pruebas** en `CameraStoreTests` (el `FakeSecretStore` no puede leer lo que no empieza por `fake:`; escribir el JSON a mano con `File.WriteAllText`):

```csharp
    [Fact]
    public void An_unreadable_password_is_kept_as_it_was_when_saving()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, $$"""[{"id":"{{id}}","name":"Patio","host":"h","passwordProtected":"keychain:{{id}}"}]""");
        var store = new CameraStore(FilePath, _secrets);

        var loaded = Assert.Single(store.Load());
        Assert.Equal("", loaded.Password);
        Assert.True(loaded.PasswordMissing);
        loaded.Name = "Patio 2";
        store.Save([loaded]);

        Assert.Contains($"\"passwordProtected\": \"keychain:{id}\"", File.ReadAllText(FilePath));
        Assert.Empty(_secrets.Forgotten);
    }

    [Fact]
    public void A_typed_password_replaces_the_unreadable_one()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, $$"""[{"id":"{{id}}","name":"Patio","host":"h","passwordProtected":"AQAAAotra"}]""");
        var store = new CameraStore(FilePath, _secrets);
        var loaded = Assert.Single(store.Load());
        loaded.Password = "nueva";
        loaded.UnreadablePassword = null;
        store.Save([loaded]);

        var again = Assert.Single(store.Load());
        Assert.Equal(("nueva", false), (again.Password, again.PasswordMissing));
    }

    [Fact]
    public void Clearing_the_password_of_a_kept_camera_forgets_its_secret()
    {
        var camera = Sample(0);
        var store = new CameraStore(FilePath, _secrets);
        store.Save([camera]);
        camera.Password = "";
        store.Save([camera]);
        Assert.Equal([camera.Id], _secrets.Forgotten);
    }
```

En `CameraDuplicateTests`: `Duplicate()` de una cámara con `UnreadablePassword = "keychain:x"` da `UnreadablePassword == null` (el secreto guardado pertenece a la otra cámara). En `CameraFormTests`: aplicar un formulario con contraseña no vacía sobre una cámara con `UnreadablePassword` deja `Result.UnreadablePassword == null`; con la contraseña vacía la conserva. En `EditedCameraTests`: añadir `nameof(Camera.UnreadablePassword)` a `FromDialog` (lo decide el diálogo: se borra al escribir otra contraseña).

- [ ] **Step 2: Ejecutar** — fallan (no compila `UnreadablePassword`).

- [ ] **Step 3: Implementar**

`Camera.cs` (después de `OriginalEncoders`):

```csharp
    /// <summary>
    /// The stored password value (cameras.json) when this system could not read it — a DPAPI value from another PC,
    /// a keychain reference copied from a Mac… — so that saving writes it back unchanged instead of erasing it.
    /// Null when the password was read (or there is none). Cleared when the user types a new password.
    /// </summary>
    public string? UnreadablePassword { get; set; }

    /// <summary>The camera has a stored password this system cannot read: the user must type it again.</summary>
    public bool PasswordMissing => UnreadablePassword is not null;
```

y en `Duplicate()`: `copy.UnreadablePassword = null;`.

`CameraStore`:
- `FromDto`: `var stored = d.PasswordProtected ?? ""; var password = secrets.Unprotect(d.Id, stored);` → `Password = password ?? ""`, `UnreadablePassword = password is null ? stored : null`.
- `ToDto`: `PasswordProtected = password.Length == 0 && c.UnreadablePassword is { } raw ? raw : secrets.Protect(c.Id, password)` (donde `password` es el que ya calcula, tras sacar las credenciales de la URL).
- `Save`: calcular primero la lista de DTO; tras escribir, además de lo que ya hace con las cámaras que se van, llamar a `secrets.Forget(id, previo)` para cada cámara **que se queda** cuyo valor previo no estaba vacío y cuyo nuevo `PasswordProtected` está vacío.

`CameraForm.TryApply`: tras `_camera.Password = fields.Password;` añadir `if (fields.Password.Length > 0) _camera.UnreadablePassword = null;`. Añadir `public const string PasswordNote = "La contraseña guardada no se puede leer en este equipo: escríbela de nuevo.";`. `AddCameraDialog`: si la cámara inicial tiene `PasswordMissing`, `HintText.Text = CameraForm.PasswordNote` al abrir (la pista de la marca sustituye a esta en cuanto cambia la marca, como hoy).

- [ ] **Step 4:** pruebas de Core, Platform y Desktop pasan; solución (también la WPF) con 0 advertencias. **Step 5: Commit** — `fix(core): conservar las contraseñas que este sistema no puede leer`.

---

### Task 3: Ayuda: Manual, Licencia, Soporte y Acerca de

**Files:**
- Create: `src/Centinela.Desktop/Services/Info/AppInfo.cs`
- Create: `src/Centinela.Desktop/Services/Info/InfoBlock.cs`
- Create: `src/Centinela.Desktop/Services/Info/ManualDocument.cs`, `src/Centinela.Desktop/Services/Info/InfoDocuments.cs`
- Create: `src/Centinela.Desktop/Services/Ui/Links.cs`
- Create: `src/Centinela.Desktop/Views/InfoWindow.axaml(.cs)`, `src/Centinela.Desktop/Views/AboutWindow.axaml(.cs)`
- Create: `src/Centinela.Desktop/Views/MainWindow.Help.cs`; Modify: `src/Centinela.Desktop/Views/MainWindow.axaml`
- Create: `tests/Centinela.Desktop.Tests/InfoDocumentsTests.cs`

**Interfaces:**
- Produces:
  - `static class AppInfo { const string Repository = "tecxion/centinela"; const string RepositoryUrl; const string WebsiteUri = "https://www.tecxart.es"; const string EmailUri = "mailto:tecxart@gmail.com"; static Version Version; static Version Normalize(Version v); }` — `Version` de `Assembly.GetEntryAssembly()`, siempre mayor.menor.parche (como `src/Centinela.App/AppInfo.cs`). La 2d (actualizaciones) lo usa.
  - Bloques (`InfoBlock.cs`): `abstract record InfoBlock; sealed record InfoHeading(string Text) : InfoBlock; sealed record InfoParagraph(string Text, string? Lead = null) : InfoBlock; sealed record InfoLink(string Text, string Uri) : InfoBlock; sealed record InfoCode(string Text) : InfoBlock;` (`Lead` va en negrita delante del texto).
  - `static class ManualDocument { static IReadOnlyList<InfoBlock> Build(); }`; `static class InfoDocuments { static IReadOnlyList<InfoBlock> License(); static IReadOnlyList<InfoBlock> Support(); }`.
  - `static class Links { static Task OpenAsync(Visual from, string uri); }` — abre con `TopLevel.GetTopLevel(from)!.Launcher.LaunchUriAsync(new Uri(uri))`; si devuelve `false` o lanza, `MessageDialog.ShowAsync(ventana, $"No se pudo abrir {uri}")`.
  - `InfoWindow(string title, IReadOnlyList<InfoBlock> blocks)`; `AboutWindow()`.

- [ ] **Step 1: Pruebas** (`InfoDocumentsTests`):
  - `AppInfo.Normalize(new Version(1, 4))` es `1.4.0`; `Normalize(new Version(1, 4, 2, 7))` es `1.4.2`.
  - El Manual empieza por `InfoHeading("Manual de Centinela")`, tiene los encabezados de la WPF en el mismo orden («Añadir cámaras», «Vistas», «Clic derecho», «Zoom», «Sonido», «Grabar y capturas», «Bandeja del sistema», «Detección de movimiento», «Registro de movimiento», «Opciones de avisos», «Copias de seguridad», «Actualizaciones», «Errores frecuentes», «Estadísticas») y, tras «Errores frecuentes», un `InfoParagraph` por cada `StreamErrorKind` con `Lead = text.Short + ": "` y `Text = text.Advice` (`ErrorTranslator.Translate(kind, Brand.Custom, "La cámara")`).
  - `License()` contiene un `InfoCode` que empieza por «MIT License» y enlaces a `https://ffmpeg.org`, `https://github.com/BtbN/FFmpeg-Builds`, `https://openal-soft.org`, `https://github.com/dotnet/Silk.NET` y `https://avaloniaui.net`; ningún bloque menciona «NAudio».
  - `Support()` enlaza `AppInfo.WebsiteUri` («www.tecxart.es») y `AppInfo.EmailUri` («tecxart@gmail.com»).

- [ ] **Step 2: Ejecutar** — fallan (no compila).

- [ ] **Step 3: Documentos** — copiar los textos de `src/Centinela.App/InfoDocuments.cs` **tal cual** (mismos párrafos, mismo orden; `H(...)` → `InfoHeading`, `P(...)` → `InfoParagraph`, `Link(...)` → `InfoLink`, la licencia MIT en `InfoCode`). `ManualDocument` lleva el Manual (en su propio archivo por tamaño); `InfoDocuments` la Licencia y el Soporte. Único cambio de texto, en la Licencia: el apartado «NAudio» se sustituye por:
  - `InfoHeading("Sonido")`, `InfoParagraph("El sonido se reproduce con OpenAL Soft, bajo licencia LGPL v2 (biblioteca separada), a través de Silk.NET (MIT).")`, `InfoLink("https://openal-soft.org", …)`, `InfoLink("https://github.com/dotnet/Silk.NET", …)`;
  - `InfoHeading("Interfaz")`, `InfoParagraph("La interfaz usa Avalonia y CommunityToolkit.Mvvm, ambos bajo licencia MIT.")`, `InfoLink("https://avaloniaui.net", …)`.

- [ ] **Step 4: Ventanas**
  - `InfoWindow`: 640×640, centrada en la dueña, título el recibido; `ScrollViewer` con un `StackPanel` (margen 24) que pinta cada bloque: `InfoHeading` → `TextBlock` negrita 18, margen `0,16,0,6`; `InfoParagraph` → `SelectableTextBlock` con ajuste de línea, tamaño 14, y si hay `Lead` un `Run` en negrita delante; `InfoLink` → `HyperlinkButton` (contenido `Text`, `Padding="0"`) cuyo `Click` llama a `Links.OpenAsync(this, Uri)` (no usar `NavigateUri`: el fallo debe avisar); `InfoCode` → `SelectableTextBlock` con `FontFamily="Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"`, tamaño 12. La conversión bloque → control va en un método privado de la vista (sin lógica).
  - `AboutWindow`: como `AboutWindow.xaml` («Acerca de Centinela», tamaño al contenido, sin redimensionar, centrada, fuera de la barra de tareas): logo `/Assets/centinela.png` 48×48, «Centinela {AppInfo.Version}» (18, seminegrita), «Visor de cámaras IP» (gris), «Licencia MIT», enlace «github.com/tecxion/centinela» (`AppInfo.RepositoryUrl`), enlace «Soporte: www.tecxart.es» (`AppInfo.WebsiteUri`), botón «Cerrar» (90 de ancho, `IsDefault` e `IsCancel`).
  - Menú **Ayuda** (`A_yuda`, tras Ver): «_Manual», «_Licencia», «_Soporte», separador, «_Acerca de Centinela». Manual/Licencia/Soporte se abren sin bloquear (`Show(this)`) con los títulos «Manual de Centinela», «Licencia», «Soporte»; Acerca de es modal (`ShowDialog(this)`). Los manejadores en `MainWindow.Help.cs`.

- [ ] **Step 5:** pruebas y compilación con 0 advertencias. **Step 6: Commit** — `feat(desktop): menú Ayuda con manual, licencia, soporte y Acerca de`.

---

### Task 4: Copia automática y «Carpeta de copia automática…»

**Files:**
- Create: `src/Centinela.Desktop/Services/Backup/AutomaticBackup.cs`
- Create: `src/Centinela.Desktop/ViewModels/MainViewModel.Backup.cs`
- Modify: `src/Centinela.Desktop/ViewModels/MainViewModel.cs` (llamar a `InitBackup()`), `src/Centinela.Desktop/Services/Data/CameraRepository.cs` (comentario de `Saved`)
- Create: `src/Centinela.Desktop/Views/MainWindow.Backup.cs`; Modify: `src/Centinela.Desktop/Views/MainWindow.axaml(.cs)`
- Create: `tests/Centinela.Desktop.Tests/AutomaticBackupTests.cs`

**Interfaces:**
- Produces:
  - `sealed class AutomaticBackup(Action<string, IReadOnlyList<Camera>> write)` — `void Schedule(string folder, IEnumerable<Camera> cameras)` (hace copias `Clone()` en el acto; hilo de interfaz), `Task IdleAsync()`, `event Action<string>? Failed` (en un hilo de fondo, con el mensaje ya redactado). Constructor por defecto para la app: `new AutomaticBackup((folder, cams) => BackupWriter.WriteAutomatic(folder, cams))`.
  - `MainViewModel`: `AutomaticBackup Backup { get; }`, `string BackupFolder` (`Settings.BackupFolder` o `AppPaths.DefaultBackupDirectory` si está vacío o en blanco), `void SetBackupFolder(string folder)` (guarda el ajuste y programa una copia).

Reglas (las de `MainWindow.Menu.cs`): una copia sin contraseñas (`camaras-copia.json`) tras cada guardado correcto de la lista de cámaras; en segundo plano; una sola escritura a la vez y, si llegan peticiones mientras escribe, se juntan en **una** escritura más con la lista más reciente; un fallo avisa en la línea de estado «No se pudo guardar la copia automática: {mensaje}» y no para las siguientes; al cerrar, la espera acotada incluye `Backup.IdleAsync()`.

- [ ] **Step 1: Pruebas** (`AutomaticBackupTests`):

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Tests;

public class AutomaticBackupTests
{
    static Camera Named(string name) => new() { Name = name, Host = name };

    [Fact]
    public async Task Requests_made_while_writing_collapse_into_one_write_of_the_latest_list()
    {
        var gate = new ManualResetEventSlim();
        var written = new List<string>();
        var backup = new AutomaticBackup((folder, cameras) =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            lock (written) written.Add(folder + ":" + string.Join(",", cameras.Select(c => c.Name)));
        });
        backup.Schedule("f", [Named("A")]);
        backup.Schedule("f", [Named("B")]);
        backup.Schedule("g", [Named("C")]);
        gate.Set();
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["f:A", "g:C"], written);
    }

    [Fact]
    public async Task The_list_is_copied_when_scheduled()
    {
        var names = new List<string>();
        var backup = new AutomaticBackup((_, cameras) => names.AddRange(cameras.Select(c => c.Name)));
        var camera = Named("A");
        backup.Schedule("f", [camera]);
        camera.Name = "cambiada";
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["A"], names);
    }

    [Fact]
    public async Task A_failed_copy_is_reported_and_the_next_one_still_runs()
    {
        var calls = 0;
        var backup = new AutomaticBackup((_, _) => { if (Interlocked.Increment(ref calls) == 1) throw new IOException("disco lleno"); });
        string? failure = null;
        backup.Failed += message => failure = message;
        backup.Schedule("f", [Named("A")]);
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        backup.Schedule("f", [Named("A")]);
        await backup.IdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("No se pudo guardar la copia automática: disco lleno", 2), (failure, calls));
    }
}
```

y una prueba de `MainViewModel` (con repositorios en carpeta temporal como `RemoveCameraTests`): `BackupFolder` es `AppPaths.DefaultBackupDirectory` con el ajuste vacío o `"  "`, y el ajuste cuando lo hay; `SetBackupFolder(x)` guarda `x` en `settings.json` (leerlo con un `SettingsStore` nuevo). No programar copias reales en esa prueba: construir el VM con un `AutomaticBackup` falso (ver Step 3).

- [ ] **Step 2: Ejecutar** — fallan.

- [ ] **Step 3: Implementar**
  - `AutomaticBackup`: puerto de `ScheduleAutomaticBackup`/`WriteAutomaticBackups`/`AutomaticBackupAsync` de `MainWindow.Menu.cs` con un `Lock`, el pendiente `(string Folder, List<Camera> Cameras)?`, `_running` y `_worker = Task.Run(Loop)`; el `catch (Exception ex)` del bucle lanza `Failed?.Invoke($"No se pudo guardar la copia automática: {ex.Message}")`.
  - `MainViewModel`: nuevo constructor opcional `MainViewModel(CameraRepository cameras, SettingsRepository settings, AutomaticBackup? backup = null)` (`backup ?? new AutomaticBackup(...)` con `BackupWriter.WriteAutomatic`). `InitBackup()` (en `MainViewModel.Backup.cs`): `Cameras.Saved += ScheduleBackup;` y `Backup.Failed += message => UiThread.Post(() => Notify(message, null));`. `ScheduleBackup` **captura sus propios errores** (pendiente de la 2a: `Saved` se lanza fuera del `try` del repositorio): `try { Backup.Schedule(BackupFolder, Cameras.All); } catch (Exception ex) { Notify($"No se pudo guardar la copia automática: {ex.Message}", null); }`.
  - Comentario de `CameraRepository.Saved`: «Raised after a successful save; listeners must catch their own errors (the automatic backup does).»
  - Menú **Archivo**: «_Carpeta de copia automática…» (la entrada de importar/exportar llega en la Task 5; dejar ya el separador antes de «_Salir»). En `MainWindow.Backup.cs`: `StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Carpeta de copia automática", SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_vm.BackupFolder) })`; con una carpeta elegida y ruta local (`TryGetLocalPath()`), `_vm.SetBackupFolder(ruta)`. Si la carpeta sugerida no existe, `TryGetFolderFromPathAsync` puede devolver null: se pasa null.
  - `OnClosed`: añadir `_vm.Backup.IdleAsync()` a la espera acotada.

- [ ] **Step 4:** pruebas y compilación con 0 advertencias. **Step 5: Commit** — `feat(desktop): copia automática y su carpeta`.

---

### Task 5: Archivo › Importar JSON y Exportar JSON

**Files:**
- Create: `src/Centinela.Desktop/Services/Backup/PassphraseRules.cs`
- Create: `src/Centinela.Desktop/Services/Backup/BackupReader.cs`
- Create: `src/Centinela.Desktop/Views/PassphraseDialog.axaml(.cs)`, `src/Centinela.Desktop/Views/ExportDialog.axaml(.cs)`
- Modify: `src/Centinela.Desktop/ViewModels/MainViewModel.Backup.cs`, `src/Centinela.Desktop/Services/Data/CameraRepository.cs`, `src/Centinela.Desktop/Views/MainWindow.Backup.cs`, `src/Centinela.Desktop/Views/MainWindow.axaml(.cs)`
- Create: `tests/Centinela.Desktop.Tests/PassphraseRulesTests.cs`, `tests/Centinela.Desktop.Tests/BackupReaderTests.cs`, `tests/Centinela.Desktop.Tests/ImportTests.cs`

**Interfaces:**
- Produces:
  - `static class PassphraseRules { const int MinimumLength = 8; static string? Validate(string first, string? second); }` — textos y orden de `PassphraseDialog.Validate` («La clave debe tener al menos 8 caracteres.», «Las claves no coinciden.», «Escribe la clave.»); `second` null = sin confirmación.
  - `sealed class BackupReader(Func<Task<string?>> askPassphrase, Func<string, Task<bool>> askRetry, Action<string> status)` con `Task<BackupReadResult> ReadAsync(string path)`; `sealed record BackupReadResult(BackupImport? Import, string? Error)` (los dos null = cancelado).
  - `CameraRepository.ReplaceAll(IEnumerable<Camera> cameras)` (sustituye la lista, en el orden dado; no guarda).
  - `MainViewModel.ApplyImport(BackupImport imported, Action<Guid> retireTiles)` → `string` (el resumen «{Added} añadidas, {Updated} actualizadas, {WithoutPassword} sin contraseña.»).
  - `PassphraseDialog.AskAsync(Window owner, bool confirm)` → `Task<string?>`; `ExportDialog` con `bool IncludePasswords` y `string? Passphrase` tras `ShowDialog<bool>(owner) == true`.

Reglas (las de `MainWindow.Menu.cs`):
- **Leer** (`BackupReader.ReadAsync`, puerto de `ReadBackupAsync`): lee el texto; si está cifrado (`CameraBackup.IsEncrypted`), pide la clave (cancelar → resultado vacío); `status("Descifrando la copia…")` o `status("Leyendo la copia…")`; `CameraBackup.Import` en `Task.Run`; `status("")` al terminar. Con `BackupPassphraseException`, `askRetry($"{ex.Message} ¿Probar con otra clave?")`: sí → vuelve a pedir la clave; no → resultado vacío. `BackupFormatException`, `IOException`, `UnauthorizedAccessException` → `status("")` y `Error = ex.Message`. `OperationCanceledException` → `status("")` y resultado vacío.
- **Aplicar** (`ApplyImport`, puerto de `ApplyImport`): `BackupMerge.Merge(Cameras.All, imported.Cameras)`; para cada Id actualizado: si graba, `Recordings.StopWithNotice(id)`, y `retireTiles(id)`; `Cameras.ReplaceAll(merge.Cameras)`; `Cameras.Save()` (dispara la copia automática); `CamerasChanged()`. Las contraseñas importadas van al almacén del sistema al guardar (lo hace `CameraStore`).
- **Importar** (ventana): una operación de copia a la vez (`_backupBusy`); `OpenFilePickerAsync` con título «Importar», filtro «Copia de Centinela» `*.json`, carpeta sugerida `_vm.BackupFolder`; leer; con error, `MessageDialog.ShowAsync(this, error)`; si se cerró la ventana mientras tanto, nada; si no, `ApplyImport(import, DisposeTilesOf)` y `MessageDialog.ShowAsync(this, resumen)`. Preguntas de la clave y de reintento sobre esta ventana (`MessageDialog.ConfirmAsync`).
- **Exportar** (ventana): `_backupBusy`; `ExportDialog`; `SaveFilePickerAsync` con título «Exportar», nombre sugerido `centinela-camaras-{DateTime.Now:yyyy-MM-dd}.json`, extensión `json`, mismo filtro y carpeta; copia de la lista (`Clone()`); `Notify("Exportando…")` o `Notify("Cifrando la copia…")`; `Task.Run(() => BackupWriter.WriteAtomic(ruta, CameraBackup.Export(copia, clave)))` guardado en `_manualExport` (la espera al cerrar lo incluye); al terminar `Notify($"Copia exportada: {ruta}", ruta)`; con `IOException`/`UnauthorizedAccessException`, `Notify("", null)` y `MessageDialog.ShowAsync(this, ex.Message)`.
- Menú **Archivo**: «_Importar JSON…», «_Exportar JSON…», «_Carpeta de copia automática…», separador, «_Salir».

- [ ] **Step 1: Pruebas**
  - `PassphraseRulesTests`: `Validate("", null)` → «Escribe la clave.»; `Validate("corta", null)` → null (sin confirmación no hay mínimo); `Validate("corta", "corta")` → «La clave debe tener al menos 8 caracteres.»; `Validate("12345678", "12345679")` → «Las claves no coinciden.»; `Validate("12345678", "12345678")` → null.
  - `BackupReaderTests` (archivos en carpeta temporal escritos con `CameraBackup.Export`): copia sin cifrar → `Import` con las cámaras y sin preguntar la clave, estados `["Leyendo la copia…", ""]`; cifrada con clave mala y luego buena → `askRetry` recibe «La clave no es correcta o el archivo está dañado. ¿Probar con otra clave?» una vez y el resultado trae las contraseñas; cancelar la clave → `(null, null)`; archivo con `{}` → `Error` no nulo y `Import` null.
  - `ImportTests` (repositorios temporales, `MainViewModel`): importar una cámara nueva y otra que coincide (mismo host y puerto) con un nombre distinto → resumen «1 añadidas, 1 actualizadas, 0 sin contraseña.» (las dos traen contraseña), `retireTiles` llamado solo con el Id de la actualizada, `cameras.json` releído tiene 3 cámaras, `ViewChanged` lanzado.

- [ ] **Step 2: Ejecutar** — fallan.

- [ ] **Step 3: Implementar** lo anterior. Diálogos:
  - `PassphraseDialog`: «Clave de la copia», 360 de ancho, alto al contenido, sin redimensionar, fuera de la barra de tareas: «Clave» + `TextBox` con `PasswordChar="●"`; en modo confirmación «Repite la clave» + otro; texto de error rojo; «Aceptar» (`IsDefault`) valida con `PassphraseRules` y cierra con la clave; «Cancelar» (`IsCancel`) cierra con null. Foco en el primer campo al abrirse.
  - `ExportDialog`: «Exportar cámaras», 400 de ancho: casilla «Incluir contraseñas (cifradas con una clave)»; debajo (sangrado 20, desactivado hasta marcarla) «Clave», «Repite la clave» y la nota gris «Guarda la clave en un lugar seguro: sin ella no se pueden recuperar las contraseñas.»; error rojo; «Exportar» (`IsDefault`) / «Cancelar» (`IsCancel`). Al marcar la casilla se borra el error y el foco va a «Clave».

- [ ] **Step 4:** pruebas y compilación con 0 advertencias. **Step 5: Commit** — `feat(desktop): importar y exportar la lista de cámaras`.

---

### Task 6: «🔍 Buscar en red»

**Files:**
- Create: `src/Centinela.Desktop/Services/Discovery/NetworkList.cs`
- Create: `src/Centinela.Desktop/Services/Discovery/DiscoveryResults.cs`
- Create: `src/Centinela.Desktop/Views/DiscoveryDialog.axaml(.cs)`
- Create: `src/Centinela.Desktop/Views/MainWindow.Discovery.cs`; Modify: `src/Centinela.Desktop/Views/MainWindow.axaml`
- Create: `tests/Centinela.Desktop.Tests/NetworkListTests.cs`, `tests/Centinela.Desktop.Tests/DiscoveryResultsTests.cs`

**Interfaces:**
- Consumes (Core): `ProbeTargets` (`NetworksOf`, `NetworkOf`, `Parse`, `Zone`), `WsDiscovery` (`LocalAddresses`, `ProbeAsync`, `SweepAsync`), `NetworkScan.FindNetworksAsync`, `DiscoveredDevice`, `OnvifClient`, `OnvifDeviceInfo`, `OnvifAuthException`, `BrandInference.FromManufacturer`.
- Produces:
  - `sealed partial class NetworkItem : ObservableObject` — `string Network`, `bool CanUncheck`, `bool Saved`, observables `IsChecked` y `Note`, `string Label => $"{Network} — {Note}"` (avisa del cambio de `Label` cuando cambia `Note`).
  - `sealed class NetworkList` — constructor `(IEnumerable<string> localNetworks, IEnumerable<string> knownHosts, IEnumerable<string> savedNetworks)` que añade, en este orden, las del PC («red de este PC», marcadas, no desmarcables, no guardables), las de las cámaras («tus cámaras», marcadas, desmarcables, no guardables) y las guardadas («guardada», marcadas, desmarcables, guardables); `ObservableCollection<NetworkItem> Items`; `NetworkItem Add(string network, string note, bool isChecked, bool canUncheck, bool saved)` (sin duplicar, ignorando mayúsculas: si ya está, solo la marca cuando se pide); `IReadOnlyList<string> SavedNetworks` (marcadas y guardables); `string TargetsText` (las marcadas que no son del PC, separadas por comas, para `ProbeTargets.Parse`); `void CheckAll()`; `bool IsLocal(string network)`; `(int WithCameras, int Empty) ApplyDetection(IReadOnlyList<DiscoveredDevice> found, IReadOnlyList<string> existingNetworks, IEnumerable<string> knownHosts)` — la parte de `Detect_Click` que añade redes: por red con cámaras, desmarcada y guardable con nota «detectada: {n} cámara(s)»; las redes existentes que no son del PC ni tienen cámaras, desmarcadas y guardables, con nota «sin cámaras ONVIF» salvo que alguna cámara ya añadida esté en ella.
  - `sealed record DiscoveryRow(DiscoveredDevice Device, string Host, string Name, string Hardware, string Status)` y `static class DiscoveryResults`: `IReadOnlyList<DiscoveryRow> Rows(IEnumerable<DiscoveredDevice> devices, IReadOnlySet<string> knownHosts)` (ordenadas por IP numérica, las que no son IP al final; `Status` «Ya añadida» o «»), `string Summary(int found, int alreadyAdded)` («{found} cámara(s) encontradas, {alreadyAdded} ya añadida(s).»), `string DisplayName(OnvifDeviceInfo? info, DiscoveryRow row)` y `Camera ToCamera(DiscoveryRow row, OnvifDeviceInfo? info, string user, string password, string main, string sub)` (como `Add_Click`: puerto de la URL principal o 554, `MainUrlOverride`/`SubUrlOverride`, marca por fabricante).

- [ ] **Step 1: Pruebas**
  - `NetworkListTests`: orden y notas del constructor; una red que es a la vez del PC y de una cámara aparece una vez, no desmarcable; `Add` de una ya listada solo la marca; `SavedNetworks` excluye las del PC y de las cámaras y las desmarcadas; `TargetsText` excluye las del PC; `ApplyDetection` con dos cámaras en `10.20.20.0/24`, una red vacía `10.30.30.0/24` y otra vacía donde ya hay una cámara añadida → devuelve `(1, 2)`, notas «detectada: 2 cámara(s)», «sin cámaras ONVIF» y la nota original de la tercera sin cambiar; todas desmarcadas. (Usar el formato de red que devuelve `ProbeTargets.NetworkOf`; leerlo de `ProbeTargetsTests` antes de escribir las cadenas.)
  - `DiscoveryResultsTests`: orden por IP (`192.168.1.20` antes que `192.168.1.100`, un nombre no IP al final); «Ya añadida» ignorando mayúsculas; `Summary(3, 1)`; `DisplayName` con modelo → «{modelo} ({host})», sin modelo con nombre → «{nombre} ({host})», sin nada → host; `ToCamera` con `rtsp://h:8554/x` → puerto 8554, y sin puerto → 554.

- [ ] **Step 2: Ejecutar** — fallan.

- [ ] **Step 3: Implementar** las dos clases puras y el diálogo, puerto de `DiscoveryDialog.xaml(.cs)` con el mismo aspecto y textos: «Buscar cámaras en la red», 680×640 (alto mínimo 520); caja «Redes donde buscar» con la lista de casillas (`ListBox` de 110 de alto, casilla ligada a `IsChecked`, activa según `CanUncheck`, contenido `Label`), caja de texto con su tooltip, «Añadir red», «📡 Detectar otras redes» (con su tooltip; mientras detecta dice «⏹ Detener» y lo cancela), «Marcar todas», barra de progreso de 160×14 visible solo al detectar, nota gris; «🔍 Buscar» y el texto de estado; la lista de cámaras sin `DataGrid`: una fila de cabecera (IP 120, Nombre 160, Modelo 190, Estado 100) y un `ListBox` cuyo elemento es un `Grid` con esas mismas columnas; abajo Usuario/Contraseña (con `PasswordChar`), la pista gris «Tapo: la «Cuenta de cámara». Imou: admin + código de seguridad.», «Añadir seleccionada» (`IsDefault`) y «Cerrar» (`IsCancel`). Busca sola al abrirse. Todos los textos de estado, el orden de las operaciones, la cancelación al cerrar (`_closed`) y los errores exactamente como la WPF. Resultado: `Camera? Result`, `IReadOnlyList<string> SavedNetworks`; cierra con `true` al añadir.
  - Barra: «🔍 Buscar en red» justo después de «➕ Añadir cámara». Al cerrarse el diálogo: si `SavedNetworks` cambió respecto a `Settings.DiscoveryNetworks`, guardarlo; si trae cámara, abrir `AddCameraAsync(found, this)` (el diálogo de añadir, relleno). Los hosts conocidos son los de `_vm.Cameras.All`.

- [ ] **Step 4:** pruebas y compilación con 0 advertencias. **Step 5: Commit** — `feat(desktop): buscar cámaras en la red`.

---

### Task 7: «Calidad de la cámara…»

**Files:**
- Create: `src/Centinela.Desktop/Services/Quality/QualityText.cs`
- Create: `src/Centinela.Desktop/Services/Quality/QualityEdits.cs`
- Create: `src/Centinela.Desktop/Services/Quality/QualityQuery.cs`
- Create: `src/Centinela.Desktop/Views/CameraQualityWindow.axaml(.cs)`
- Modify: `src/Centinela.Desktop/Views/MainWindow.Cameras.cs`
- Create: `tests/Centinela.Desktop.Tests/QualityTextTests.cs`, `tests/Centinela.Desktop.Tests/QualityEditsTests.cs`

**Interfaces:**
- Consumes (Core/Media): `OnvifClient`, `DahuaApi`, `EncoderSettings`, `EncoderOptions`, `EncoderChange`, `EncoderSnapshot`, `CredentialSanitizer`.
- Produces:
  - `static class QualityText { static string Describe(EncoderSettings s); static string Range(int min, int max); static string Explain(Exception ex); static string HostOf(Camera camera); static string Values(EncoderChange c); }` — `Values` es «{W}×{H} · {fps} fps · {kbps} kbps».
  - `static class QualityEdits` — `const int MinBitrate = 32, MaxBitrate = 16384`; `(int Min, int Max) FpsRange(EncoderOptions? options)` (`Max(1, FpsMin)` y `FpsMax` o 30); `EncoderChange Recommended(IReadOnlyList<(int W, int H)> choices, EncoderOptions? options, bool main)`; `string? TryRead(string token, (int W, int H)? resolution, string fpsText, string bitrateText, EncoderOptions? options, out EncoderChange change)` (null = válido; si no, el mensaje de la WPF); `IReadOnlyList<(int W, int H)> Choices(EncoderSettings current, EncoderOptions? options)` (las que admite, con la actual delante si no está).
  - `sealed class QualityQuery(Camera camera)` — `Task<IReadOnlyList<(string Token, EncoderSettings Settings, EncoderOptions? Options)>> RunAsync(bool includeApi, Action<string> line, CancellationToken ct)` (las líneas del informe de `QueryAsync`, sin la contraseña) y `Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<(string Token, EncoderChange Change)> changes)` (una línea por cambio, «enviado … — aceptado.» o «la cámara no lo aceptó — …»).
  - `CameraQualityWindow(Camera camera, Action<string> saveOriginals)` — `saveOriginals` recibe el texto de `EncoderSnapshot.Format` la primera vez que se aplica un cambio.

- [ ] **Step 1: Pruebas**
  - `QualityTextTests`: `Describe` con todos los campos → «Main · H264 · 1920×1080 · 25 fps · 2048 kbps CBR · GOP 50»; sin nombre → empieza por «(sin nombre)»; campos a 0 se omiten; `Range(0, 0)` → «?», `Range(1, 25)` → «1–25»; `Explain(new TaskCanceledException())` → «no contesta (tiempo agotado)»; `Explain` de un `HttpRequestException` con `rtsp://u:p@h` en el mensaje no contiene `p@` (lo limpia `CredentialSanitizer`); `HostOf` con `Host` vacío saca el host de la URL principal.
  - `QualityEditsTests`: `Recommended` principal con [1920×1080, 1280×720, 640×360] → 1280×720, 10 fps, 768; secundario → 640×360, 10, 256; sin ninguna ≤ límite → la más pequeña; fps limitado por `FpsMax = 8` → 8; `TryRead` con fps «abc» → «Revisa los valores de {token}: fps y bitrate deben ser números enteros.»; con fps 40 y máximo 30 → «{token}: los fps deben estar entre 1 y 30, y el bitrate entre 32 y 16384 kbps.»; válido → null y el cambio; `Choices` pone la actual delante cuando no está entre las admitidas.

- [ ] **Step 2: Ejecutar** — fallan.

- [ ] **Step 3: Implementar** — puerto de `CameraQualityWindow.xaml.cs` repartido: los textos y reglas en `QualityText`/`QualityEdits`, la consulta y la aplicación en `QualityQuery` (mismos tiempos: ONVIF 20 s, API 8 s, consulta entera 120 s; mismas líneas y el mismo orden), la ventana solo con la interfaz: «Calidad de la cámara — {nombre}», 760×640 (alto mínimo 480); el texto de arriba; el informe en un `TextBox` de solo lectura, monoespaciado 12, sin ajuste y con barras; la caja «Cambiar calidad (ONVIF)» desactivada mientras consulta o sin filas, con la tabla de editores construida en código (cabeceras «Flujo», «Resolución», «fps», «Bitrate (kbps)», «Ahora»; filas «Principal ({token})» y «Secundario ({token})» ordenadas por píxeles, desplegable de resoluciones, cajas de fps y bitrate, valores actuales en gris), la nota, «Recomendados (wifi pobre)», «Valores originales» (activo si hay originales) y «Aplicar…» con sus tooltips; abajo «Consultar otra vez», «Copiar» (portapapeles de Avalonia: `TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(texto)`; el informe nunca lleva la contraseña) y «Cerrar». Avisos y la confirmación de «Aplicar…» con `MessageDialog` y los mismos textos (la confirmación con «Sí/No»). Al cerrar se cancela la consulta en curso. La primera vez que se aplica, guarda los originales con `saveOriginals` y los recuerda en la ventana para «Valores originales».
  - Conectar en `MainWindow.Cameras.cs`: `tile.EncoderInfoRequested += t => ShowQuality(t)` que abre, sin bloquear, `new CameraQualityWindow(camara actual, text => SaveOriginals(id, text)).Show(OwnerFor(t))`. `SaveOriginals` busca la cámara guardada por Id (puede haber sido editada o reemplazada mientras tanto) y, si aún no tiene originales, los pone y guarda (`_vm.Cameras.Save()`).

- [ ] **Step 4:** pruebas y compilación con 0 advertencias. **Step 5: Commit** — `feat(desktop): calidad de la cámara por ONVIF`.

---

### Task 8: Ver › «Opciones de avisos…»

**Files:**
- Create: `src/Centinela.Desktop/ViewModels/NotificationOptionsRow.cs`
- Create: `src/Centinela.Desktop/Services/Notifications/NotificationOptions.cs`
- Create: `src/Centinela.Desktop/Views/NotificationOptionsWindow.axaml(.cs)`
- Create: `src/Centinela.Desktop/Views/MainWindow.Options.cs`; Modify: `src/Centinela.Desktop/Views/MainWindow.axaml`
- Create: `tests/Centinela.Desktop.Tests/NotificationOptionsTests.cs`

**Interfaces:**
- Produces:
  - `sealed record OptionItem<T>(T Value, string Label)`.
  - `sealed partial class NotificationOptionsRow : ObservableObject` — `Guid Id`, `string Name`; observables `MotionEnabled`, `Sensitivity` (`OptionItem<MotionSensitivity>`), `Cooldown` (`OptionItem<int>`), `ConnectionAlerts`, `MotionAlerts`; constructor desde una `Camera` (espera normalizada con `Camera.NormalizeCooldown`).
  - `sealed record QuietHours(bool SoundOnConnectionLost, bool SoundOnMotion, bool Enabled, string From, string To)`.
  - `static class NotificationOptions` — `IReadOnlyList<OptionItem<MotionSensitivity>> Sensitivities` («Baja», «Media», «Alta»); `IReadOnlyList<OptionItem<int>> Cooldowns` («30 s», «1 min», «5 min», «15 min», de `Camera.AllowedCooldowns`); `IReadOnlyList<NotificationOptionsRow> RowsFor(IEnumerable<Camera> cameras)` (por `Order`); `bool IsValidTime(string text)` (`AppSettings.TryParseTime` tras recortar); `const string TimeError = "Escribe las horas de silencio con el formato HH:mm (por ejemplo 23:00)."`; `void Apply(IEnumerable<NotificationOptionsRow> rows, IEnumerable<Camera> cameras, AppSettings settings, QuietHours sounds)` (copia cada fila a la cámara con su Id —las que ya no existen se ignoran— y los sonidos/horas a los ajustes, horas recortadas).

- [ ] **Step 1: Pruebas** (`NotificationOptionsTests`): `RowsFor` ordena por `Order` y normaliza una espera de 45 a 60 («1 min»); `Apply` escribe los cinco campos en la cámara correcta, ignora una fila cuyo Id ya no existe y pone los cinco ajustes (con `" 23:30 "` guardado como «23:30»); `IsValidTime`: «07:00» y « 23:59 » válidos; «24:00», «7:00», «ab:cd» y «» no; las etiquetas de `Cooldowns` son exactamente «30 s», «1 min», «5 min», «15 min».

- [ ] **Step 2: Ejecutar** — fallan.

- [ ] **Step 3: Implementar** — la ventana, puerto de `NotificationOptionsWindow.xaml(.cs)` sin `DataGrid`: «Opciones de avisos», 820×520 (mínimo 620×400), centrada, fuera de la barra de tareas; tabla = fila de cabecera («Cámara», «Detección», «Sensibilidad», «Espera», «Avisos de conexión», «Avisos de movimiento») + `ItemsControl` dentro de un `ScrollViewer` cuyo elemento es un `Grid` con las mismas columnas (nombre, casilla centrada, desplegable de 80+ con `Sensitivities`, desplegable con `Cooldowns`, dos casillas centradas; los desplegables ligados a `Sensitivity`/`Cooldown` por referencia, con `DisplayMemberBinding` a `Label`); debajo «Sonar al perder una conexión», «Sonar al detectar movimiento», «Horas de silencio» desde/hasta (cajas de 60 con `MaxLength` 5 y los tooltips de la WPF; borde rojo mientras la hora no es válida), el error rojo, la nota gris «La detección mantiene abierta la conexión de baja calidad de cada cámara vigilada, también con la app en la bandeja.», y «Aceptar» (`IsDefault`) / «Cancelar» (`IsCancel`). «Aceptar» con una hora mala muestra `TimeError` y pone el foco en la caja mala; si todo es válido, cierra con `true`. La ventana trabaja sobre copias: no cambia nada hasta que el llamador aplica.
  - Menú **Ver**: separador y «_Opciones de avisos…» tras «Mostrar _estadísticas». En `MainWindow.Options.cs`: `ShowDialog<bool>(this)`; si es `true` y la ventana no se está cerrando, `NotificationOptions.Apply(...)` sobre `_vm.Cameras.All` y `_vm.Settings.Settings`, `_vm.Cameras.Save()`, `_vm.Settings.Save()` y, en todos los mosaicos (`AllTiles()`), `_actions.Show(tile.ViewModel)` para que el 👁 refleje la detección. (La detección en sí llega en 2d.)

- [ ] **Step 4:** pruebas y compilación con 0 advertencias. **Step 5: Commit** — `feat(desktop): opciones de avisos`.

---

### Task 9: Comprobación de la fase 2c

- [ ] Pruebas de Core, Platform, Media y Desktop pasan; solución con 0 advertencias (la WPF también compila); nada de APIs de Windows en Desktop (`grep` de `System.Windows`, `Microsoft.Win32`, `Registry`, `DllImport` en `src/Centinela.Desktop`); ningún archivo nuevo de Desktop pasa de ~300 líneas.
- [ ] Añadir a `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md`, en «Fases 3–4»: adaptar el Manual (`ManualDocument`) a cada sistema.
- [ ] **Prueba del usuario** (cerrando antes el Centinela WPF): Ayuda (Manual, Licencia, Soporte con sus enlaces, Acerca de); Archivo › Exportar (sin y con contraseñas) e Importar esa copia (clave mala y buena); «Carpeta de copia automática…» y que `camaras-copia.json` se actualiza al cambiar una cámara; «🔍 Buscar en red» (redes, «Detectar otras redes», añadir una cámara encontrada); «Calidad de la cámara…» (consultar, copiar; aplicar solo si quiere); Ver › «Opciones de avisos…» (guardar y volver a abrir).
