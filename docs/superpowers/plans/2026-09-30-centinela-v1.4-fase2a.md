# Centinela v1.4 — Fase 2a: interfaz Avalonia (esqueleto, vídeo y vistas) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un `Centinela.Desktop` (Avalonia) que arranca en Windows, muestra las cámaras guardadas en todas las vistas (cuadrícula, principal + miniaturas a derecha/izquierda, dos principales), con clic para elegir la grande, doble clic a pantalla completa, arrastrar para reordenar, F11, estadísticas y cambio de flujo sin pantallazo negro. La WPF (`Centinela.App`) sigue intacta al lado.

**Architecture:** MVVM ligero. La lógica sin interfaz (elección de vista, colocación de mosaicos, textos de estado, datos) vive en clases puras con pruebas; las vistas (`.axaml` + code-behind) solo pintan e interactúan. Cada mosaico tiene un `TileViewModel` dueño de su sesión (propia o arrendada con `SharedStreams`) y un `CameraTileView` que la pinta con `VideoSurface`. `TileHost` coloca los mosaicos en la rejilla y hace el relevo sin pantallazo negro.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.22 (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent), CommunityToolkit.Mvvm 8.4.2, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (fase 2, primera parte). Pendientes arrastrados: `docs/superpowers/plans/2026-09-29-v1.4-pendientes.md`.

**Fases 2b–2d** (planes aparte): 2b botones y menú de cada cámara (HD/SD, zoom, captura, grabar, audio OpenAL, suavizar, movimiento, editar/duplicar/eliminar); 2c diálogos; 2d bandeja, avisos, movimiento, registros, actualizaciones, paridad y borrado de la WPF.

**Decisión (versión de Avalonia):** el diseño pide «la última estable, fijada en el plan». Se fija **11.3.22** (última de la rama 11) y no la 12.x: su API es la conocida y estable; pasar a la 12 queda para después, acotado a Desktop.

## Global Constraints

- .NET 10. `Centinela.Desktop` y `Centinela.Desktop.Tests`: `net10.0` (heredado de `Directory.Build.props`). Avalonia **11.3.22**, CommunityToolkit.Mvvm **8.4.2**.
- Ejecutable: `AssemblyName` = `Centinela`, `OutputType` = `WinExe`.
- Textos visibles en español, idénticos a los de la WPF donde existan.
- **Un archivo, una responsabilidad**; ningún archivo de más de ~300 líneas; las vistas sin lógica de negocio (esa va en ViewModels/servicios).
- La interfaz no llama al sistema directamente: todo lo de sistema llega por `PlatformServices` (Tasks de fase 1).
- Nada nuevo mete búfer ni retraso en el vídeo: el fotograma más reciente del buzón se pinta en el siguiente refresco (sin colas).
- La WPF (`src/Centinela.App`) **no se toca** en esta fase.
- Contraseñas nunca en texto claro en registros ni UI.
- Pruebas solo de código (lógica pura); las pruebas de uso las hace el usuario. Las pruebas no tocan datos reales (`CENTINELA_DATA_DIR`, carpetas temporales).
- Compilar Desktop y App siempre con `-p:BaseOutputPath=C:/Users/PCgaming/AppData/Local/Temp/claude/X--project1-camara-win/30c3d4fb-5b9f-42d9-8a10-21b243cdd2ca/scratchpad/bin/`. Ejecutar las pruebas desde su salida normal (algunas buscan `Centinela.slnx` subiendo carpetas). No tocar ningún Centinela en marcha.
- `git add` con rutas explícitas; commits terminados en línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Cero advertencias de compilación.

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/Centinela.Desktop/Centinela.Desktop.csproj` | Proyecto Avalonia. |
| `src/Centinela.Desktop/Program.cs` | `Main`, `BuildAvaloniaApp`. |
| `src/Centinela.Desktop/App.axaml(.cs)` | Tema, recursos y arranque (plataforma, instancia única, FFmpeg, ventana). |
| `src/Centinela.Desktop/Assets/centinela.png`, `centinela.ico` | Icono (copiados de la WPF). |
| `src/Centinela.Desktop/Services/Data/CameraRepository.cs` | Cámaras: cargar, guardar, buscar la actual por Id. |
| `src/Centinela.Desktop/Services/Data/SettingsRepository.cs` | Ajustes: cargar, guardar sin interrumpir. |
| `src/Centinela.Desktop/Services/Layout/LayoutChoices.cs` | Opciones del desplegable «Vista» ↔ ajustes. |
| `src/Centinela.Desktop/Services/Layout/LayoutController.cs` | Qué mosaicos van dónde; clic y soltar. |
| `src/Centinela.Desktop/Services/Layout/PlacedTile.cs` | Un mosaico planificado. |
| `src/Centinela.Desktop/Services/Errors/ErrorText.cs` | Traducción de errores (incl. «Falta contraseña»). |
| `src/Centinela.Desktop/Services/Streams/StreamLeases.cs` | Sesiones compartidas de calidad baja. |
| `src/Centinela.Desktop/Services/Tiles/TileHost.cs` | Colocar, reutilizar, crear y retirar mosaicos en la rejilla; relevo sin pantallazo negro. |
| `src/Centinela.Desktop/Services/Tiles/TileFactory.cs` | Crear un mosaico (VM + vista) ya conectado. |
| `src/Centinela.Desktop/Services/Ui/UiThread.cs` | Publicar en el hilo de interfaz. |
| `src/Centinela.Desktop/Services/Ui/WindowPlacement.cs` | Posición/tamaño de la ventana válidos. |
| `src/Centinela.Desktop/ViewModels/TileStatus.cs` | Texto de estado de un mosaico (puro). |
| `src/Centinela.Desktop/ViewModels/TileViewModel.cs` | Estado de un mosaico y su sesión. |
| `src/Centinela.Desktop/ViewModels/MainViewModel.cs` | Estado de la ventana principal. |
| `src/Centinela.Desktop/Video/VideoSurface.cs` | Pinta el último fotograma del buzón. |
| `src/Centinela.Desktop/Views/CameraTileView.axaml(.cs)` | Un mosaico: vídeo, nombre, estado, estadísticas; clic, doble clic, arrastrar. |
| `src/Centinela.Desktop/Views/MainWindow.axaml(.cs)` | Menú, barra, rejilla, estado. |
| `src/Centinela.Desktop/Views/FullscreenWindow.axaml(.cs)` | Una cámara a pantalla completa. |
| `src/Centinela.Desktop/Views/MessageDialog.cs` | Mensaje simple (Aceptar / Sí-No). |
| `tests/Centinela.Desktop.Tests/*` | Pruebas de la lógica pura. |

---

### Task 1: Proyecto Centinela.Desktop, pruebas y arranque vacío

**Files:**
- Create: `src/Centinela.Desktop/Centinela.Desktop.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `Views/MessageDialog.cs`, `Services/Ui/UiThread.cs`
- Create (copiar): `src/Centinela.Desktop/Assets/centinela.png`, `src/Centinela.Desktop/Assets/centinela.ico` (desde `src/Centinela.App/Assets/`)
- Create: `tests/Centinela.Desktop.Tests/Centinela.Desktop.Tests.csproj`, `tests/Centinela.Desktop.Tests/SmokeTests.cs`
- Modify: `Centinela.slnx`

**Interfaces:**
- Produces: `static class UiThread { static void Post(Action action); }`; `static class MessageDialog { static Task ShowAsync(Window? owner, string text); static Task<bool> ConfirmAsync(Window owner, string text); }`; `App.Platform` (`PlatformServices`, estático); `MainWindow` vacío con título «Centinela».

- [ ] **Step 1: Crear `src/Centinela.Desktop/Centinela.Desktop.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <AssemblyName>Centinela</AssemblyName>
    <RootNamespace>Centinela.Desktop</RootNamespace>
    <ApplicationIcon>Assets\centinela.ico</ApplicationIcon>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <CopyFFmpeg>true</CopyFFmpeg>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.22" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.22" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.22" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Centinela.Core\Centinela.Core.csproj" />
    <ProjectReference Include="..\Centinela.Media\Centinela.Media.csproj" />
    <ProjectReference Include="..\Centinela.Platform\Centinela.Platform.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Centinela.Desktop.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Copiar los iconos**

```bash
mkdir -p src/Centinela.Desktop/Assets
cp src/Centinela.App/Assets/centinela.png src/Centinela.App/Assets/centinela.ico src/Centinela.Desktop/Assets/
```

- [ ] **Step 3: `Program.cs`**

```csharp
using Avalonia;

namespace Centinela.Desktop;

static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnMainWindowClose);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
```

(Si `WithInterFont` no existe sin el paquete `Avalonia.Fonts.Inter`, quitar esa llamada: se usará la fuente del sistema.)

- [ ] **Step 4: `Services/Ui/UiThread.cs`**

```csharp
using Avalonia.Threading;

namespace Centinela.Desktop.Services.Ui;

/// <summary>Runs work on the UI thread; session events arrive on their own threads.</summary>
static class UiThread
{
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
```

- [ ] **Step 5: `Views/MessageDialog.cs`** (Avalonia no trae cuadro de mensaje)

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Centinela.Desktop.Views;

/// <summary>Small modal message: «Aceptar», or «Sí» / «No». Without an owner it shows as its own window.</summary>
static class MessageDialog
{
    public static async Task ShowAsync(Window? owner, string text) => await Build(owner, text, confirm: false);

    public static async Task<bool> ConfirmAsync(Window owner, string text) => await Build(owner, text, confirm: true);

    static Task<bool> Build(Window? owner, string text, bool confirm)
    {
        var result = new TaskCompletionSource<bool>();
        var window = new Window
        {
            Title = "Centinela",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
            CanResize = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button Add(string label, bool value, bool isDefault = false)
        {
            var button = new Button { Content = label, MinWidth = 80, IsDefault = isDefault, IsCancel = !value };
            button.Click += (_, _) =>
            {
                result.TrySetResult(value);
                window.Close();
            };
            buttons.Children.Add(button);
            return button;
        }
        if (confirm)
        {
            Add("Sí", true, isDefault: true);
            Add("No", false);
        }
        else Add("Aceptar", true, isDefault: true);

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        window.Closed += (_, _) => result.TrySetResult(false);
        if (owner is null) window.Show();
        else _ = window.ShowDialog(owner);
        return result.Task;
    }
}
```

- [ ] **Step 6: `App.axaml`**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Centinela.Desktop.App"
             RequestedThemeVariant="Dark">
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

- [ ] **Step 7: `App.axaml.cs`** (mismo orden que `Centinela.App/App.xaml.cs`; en 2a cerrar la ventana cierra el programa; la bandeja llega en 2d)

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Centinela.Core;
using Centinela.Desktop.Services.Ui;
using Centinela.Desktop.Views;
using Centinela.Media;
using Centinela.Platform;

namespace Centinela.Desktop;

public partial class App : Application
{
    internal static PlatformServices Platform { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) Start(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            Platform = PlatformServices.Create(Environment.ProcessPath!);
        }
        catch (PlatformNotSupportedException ex)
        {
            Fail(desktop, ex.Message);
            return;
        }
        if (!Platform.SingleInstance.TryClaim())
        {
            // The running instance shows its window; this one has nothing to do.
            desktop.Shutdown(0);
            return;
        }
        try
        {
            FFmpegLoader.Initialize();
        }
        catch (Exception ex)
        {
            Fail(desktop, ex.Message);
            return;
        }
        // The CamaraWin rename only ever happened on Windows.
        if (OperatingSystem.IsWindows() && !AppPaths.IsDataDirectoryOverridden)
        {
            try { LegacyData.Migrate(LegacyData.Directory, AppPaths.DataDirectory); }
            catch (Exception) { /* start empty; the user can import a copy */ }
        }
        try
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            Platform.SingleInstance.ListenForActivation(() => UiThread.Post(window.BringToFront));
        }
        catch (Exception ex)
        {
            Fail(desktop, $"No se pudo abrir Centinela: {ex.Message}");
        }
    }

    /// <summary>Shows why Centinela cannot start and ends the process once the message is closed.</summary>
    static void Fail(IClassicDesktopStyleApplicationLifetime desktop, string message) =>
        _ = MessageDialog.ShowAsync(null, message).ContinueWith(_ => UiThread.Post(() => desktop.Shutdown(1)));
}
```

- [ ] **Step 8: `Views/MainWindow.axaml` y `.axaml.cs` provisionales** (la Task 7 los completa)

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Centinela.Desktop.Views.MainWindow"
        Title="Centinela" Width="1280" Height="800" Background="#111111"
        Icon="/Assets/centinela.ico" />
```

```csharp
using Avalonia.Controls;

namespace Centinela.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Another launch asked for the window: show it on top.</summary>
    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
```

- [ ] **Step 9: Proyecto de pruebas** `tests/Centinela.Desktop.Tests/Centinela.Desktop.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Centinela.Desktop\Centinela.Desktop.csproj" />
  </ItemGroup>

</Project>
```

`tests/Centinela.Desktop.Tests/SmokeTests.cs` (se borra en la Task 2):

```csharp
namespace Centinela.Desktop.Tests;

public class SmokeTests
{
    [Fact]
    public void Test_assembly_runs() =>
        Assert.Equal("Centinela.Desktop.Tests", typeof(SmokeTests).Assembly.GetName().Name);
}
```

- [ ] **Step 10: Añadir a `Centinela.slnx`** `src/Centinela.Desktop/Centinela.Desktop.csproj` (carpeta `/src/`) y `tests/Centinela.Desktop.Tests/Centinela.Desktop.Tests.csproj` (carpeta `/tests/`).

- [ ] **Step 11: Verificar**

Run: `dotnet build src/Centinela.Desktop -p:BaseOutputPath=<scratch>/bin/ -nologo -v q` → `0 Advertencias`, `0 Errores`.
Run: `dotnet test tests/Centinela.Desktop.Tests -nologo -v q` → 1 correcta.

- [ ] **Step 12: Commit**

```bash
git add Centinela.slnx src/Centinela.Desktop/Centinela.Desktop.csproj src/Centinela.Desktop/Program.cs src/Centinela.Desktop/App.axaml src/Centinela.Desktop/App.axaml.cs src/Centinela.Desktop/Views/MainWindow.axaml src/Centinela.Desktop/Views/MainWindow.axaml.cs src/Centinela.Desktop/Views/MessageDialog.cs src/Centinela.Desktop/Services/Ui/UiThread.cs src/Centinela.Desktop/Assets/centinela.png src/Centinela.Desktop/Assets/centinela.ico tests/Centinela.Desktop.Tests/Centinela.Desktop.Tests.csproj tests/Centinela.Desktop.Tests/SmokeTests.cs
git commit -m "feat: proyecto Centinela.Desktop (Avalonia) con su arranque"
```

---

### Task 2: Datos: cámaras y ajustes

**Files:**
- Create: `src/Centinela.Desktop/Services/Data/CameraRepository.cs`, `src/Centinela.Desktop/Services/Data/SettingsRepository.cs`
- Create: `tests/Centinela.Desktop.Tests/CameraRepositoryTests.cs`, `tests/Centinela.Desktop.Tests/FakeSecretStore.cs`
- Delete: `tests/Centinela.Desktop.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: `CameraStore(string, ISecretStore)`, `SettingsStore(string)`, `AppSettings` (Core).
- Produces:
  - `sealed class CameraRepository(CameraStore store) { IReadOnlyList<Camera> All; void Load(); Camera? Find(Guid id); Camera CurrentOr(Camera shown); void Add(Camera camera); void Replace(Camera updated); void Remove(Guid id); bool Save(); event Action<string>? SaveFailed; event Action? Saved; }` — `Load` normaliza `Order` a 0..n-1; `Save` devuelve false y lanza `SaveFailed` con el texto «No se pudieron guardar las cámaras: …».
  - `sealed class SettingsRepository(SettingsStore store) { AppSettings Settings; void Save(); }` — `Save` nunca lanza.

- [ ] **Step 1: Pruebas** — `tests/Centinela.Desktop.Tests/FakeSecretStore.cs`:

```csharp
using Centinela.Core.Platform;

namespace Centinela.Desktop.Tests;

sealed class FakeSecretStore : ISecretStore
{
    public string Protect(Guid cameraId, string password) => password.Length == 0 ? "" : "fake:" + password;
    public string? Unprotect(Guid cameraId, string storedValue) =>
        storedValue.Length == 0 ? "" : storedValue.StartsWith("fake:", StringComparison.Ordinal) ? storedValue[5..] : null;
    public void Forget(Guid cameraId, string storedValue) { }
}
```

`tests/Centinela.Desktop.Tests/CameraRepositoryTests.cs`:

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Data;

namespace Centinela.Desktop.Tests;

public sealed class CameraRepositoryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());
    string FilePath => Path.Combine(_dir, "cameras.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    CameraRepository NewRepository() => new(new CameraStore(FilePath, new FakeSecretStore()));

    [Fact]
    public void Load_orders_cameras_and_renumbers_them_from_zero()
    {
        new CameraStore(FilePath, new FakeSecretStore()).Save([
            new Camera { Name = "B", Host = "b", Order = 7 }, new Camera { Name = "A", Host = "a", Order = 3 }]);
        var repo = NewRepository();
        repo.Load();
        Assert.Equal(["A", "B"], repo.All.Select(c => c.Name));
        Assert.Equal([0, 1], repo.All.Select(c => c.Order));
    }

    [Fact]
    public void Add_goes_last_replace_and_remove_by_id()
    {
        var repo = NewRepository();
        repo.Load();
        var a = new Camera { Name = "A", Host = "a" };
        var b = new Camera { Name = "B", Host = "b" };
        repo.Add(a);
        repo.Add(b);
        Assert.Equal([0, 1], repo.All.Select(c => c.Order));
        var edited = b.Clone();
        edited.Name = "B2";
        repo.Replace(edited);
        Assert.Equal("B2", repo.Find(b.Id)!.Name);
        repo.Remove(a.Id);
        Assert.Equal(["B2"], repo.All.Select(c => c.Name));
    }

    [Fact]
    public void CurrentOr_prefers_the_stored_camera()
    {
        var repo = NewRepository();
        repo.Load();
        var a = new Camera { Name = "A", Host = "a" };
        repo.Add(a);
        var old = a.Clone();
        old.Name = "old copy";
        Assert.Same(a, repo.CurrentOr(old));
        var deleted = new Camera { Name = "gone" };
        Assert.Same(deleted, repo.CurrentOr(deleted));
    }

    [Fact]
    public void Save_writes_and_reports_failures_without_throwing()
    {
        var repo = NewRepository();
        repo.Load();
        repo.Add(new Camera { Name = "A", Host = "a" });
        var saved = 0;
        repo.Saved += () => saved++;
        Assert.True(repo.Save());
        Assert.Equal(1, saved);
        Assert.True(File.Exists(FilePath));

        // A directory where the file should be makes the save fail.
        File.Delete(FilePath);
        Directory.CreateDirectory(FilePath);
        string? failure = null;
        repo.SaveFailed += message => failure = message;
        Assert.False(repo.Save());
        Assert.StartsWith("No se pudieron guardar las cámaras: ", failure);
    }
}
```

Borrar `tests/Centinela.Desktop.Tests/SmokeTests.cs`.

- [ ] **Step 2: Ejecutar para ver que fallan** — `dotnet test tests/Centinela.Desktop.Tests -nologo -v q` → error de compilación.

- [ ] **Step 3: Implementar** — `Services/Data/CameraRepository.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Desktop.Services.Data;

/// <summary>The camera list in memory and on disk. UI thread only.</summary>
sealed class CameraRepository(CameraStore store)
{
    readonly List<Camera> _cameras = [];

    public IReadOnlyList<Camera> All => _cameras;

    /// <summary>Raised after a successful save (the automatic backup listens, phase 2c).</summary>
    public event Action? Saved;
    /// <summary>Raised with a Spanish message when saving fails.</summary>
    public event Action<string>? SaveFailed;

    public void Load()
    {
        _cameras.Clear();
        _cameras.AddRange(store.Load());
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;
    }

    public Camera? Find(Guid id) => _cameras.FirstOrDefault(c => c.Id == id);

    /// <summary>The stored camera with this Id (a view may hold an older copy); the given one if it was deleted.</summary>
    public Camera CurrentOr(Camera shown) => Find(shown.Id) ?? shown;

    public void Add(Camera camera)
    {
        camera.Order = _cameras.Count == 0 ? 0 : _cameras.Max(c => c.Order) + 1;
        _cameras.Add(camera);
    }

    public void Replace(Camera updated)
    {
        var index = _cameras.FindIndex(c => c.Id == updated.Id);
        if (index >= 0) _cameras[index] = updated;
    }

    public void Remove(Guid id) => _cameras.RemoveAll(c => c.Id == id);

    public bool Save()
    {
        try
        {
            store.Save(_cameras);
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke($"No se pudieron guardar las cámaras: {ex.Message}");
            return false;
        }
        Saved?.Invoke();
        return true;
    }
}
```

`Services/Data/SettingsRepository.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Desktop.Services.Data;

/// <summary>The settings in memory and on disk.</summary>
sealed class SettingsRepository(SettingsStore store)
{
    public AppSettings Settings { get; } = store.Load();

    /// <summary>Losing a preference (window placement, layout) must never interrupt the user or block closing.</summary>
    public void Save()
    {
        try
        {
            store.Save(Settings);
        }
        catch (Exception)
        {
            // Ignored on purpose.
        }
    }
}
```

- [ ] **Step 4: Verificar** — `dotnet test tests/Centinela.Desktop.Tests -nologo -v q` → todas pasan.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Desktop/Services/Data/CameraRepository.cs src/Centinela.Desktop/Services/Data/SettingsRepository.cs tests/Centinela.Desktop.Tests/CameraRepositoryTests.cs tests/Centinela.Desktop.Tests/FakeSecretStore.cs
git rm tests/Centinela.Desktop.Tests/SmokeTests.cs
git commit -m "feat(desktop): repositorios de cámaras y ajustes"
```

---

### Task 3: Vistas: opciones del desplegable y controlador de colocación

**Files:**
- Create: `src/Centinela.Desktop/Services/Layout/LayoutChoices.cs`, `PlacedTile.cs`, `LayoutController.cs`
- Create: `tests/Centinela.Desktop.Tests/LayoutChoicesTests.cs`, `tests/Centinela.Desktop.Tests/LayoutControllerTests.cs`

**Interfaces:**
- Consumes: `ViewPlanner.Plan`, `ViewPlanner.StreamFor`, `DualSelection`, `DualState`, `AppSettings` (Core).
- Produces:
  - `static class LayoutChoices { static IReadOnlyList<string> Labels; static int IndexFor(AppSettings s); static void Apply(AppSettings s, int index); }` — etiquetas exactamente: `Auto`, `1`, `4`, `9`, `16`, `Principal + miniaturas (derecha)`, `Principal + miniaturas (izquierda)`, `Dos principales + miniaturas`.
  - `readonly record struct PlacedTile(Guid CameraId, StreamKind Stream, bool Big, int Row, int Column, int RowSpan, int ColumnSpan)`.
  - `sealed record LayoutPlan(int Rows, int Columns, IReadOnlyList<PlacedTile> Tiles)`.
  - `sealed class LayoutController(IReadOnlyList<Camera> cameras, AppSettings settings) { LayoutPlan Plan(); bool IsFeaturedLayout; IReadOnlyList<Guid> BigCameraIds(); bool Click(Guid cameraId); DropOutcome Drop(Guid source, Guid target); }` y `enum DropOutcome { None, SettingsChanged, OrderChanged }`.

- [ ] **Step 1: Pruebas** — `tests/Centinela.Desktop.Tests/LayoutChoicesTests.cs`:

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Layout;

namespace Centinela.Desktop.Tests;

public class LayoutChoicesTests
{
    [Fact]
    public void Labels_match_the_windows_version()
    {
        Assert.Equal(["Auto", "1", "4", "9", "16", "Principal + miniaturas (derecha)",
            "Principal + miniaturas (izquierda)", "Dos principales + miniaturas"], LayoutChoices.Labels);
    }

    [Theory]
    [InlineData(0, LayoutMode.Grid, GridMode.Auto)]
    [InlineData(2, LayoutMode.Grid, GridMode.Four)]
    [InlineData(4, LayoutMode.Grid, GridMode.Sixteen)]
    [InlineData(5, LayoutMode.Featured, GridMode.Auto)]
    [InlineData(6, LayoutMode.FeaturedLeft, GridMode.Auto)]
    [InlineData(7, LayoutMode.Dual, GridMode.Auto)]
    public void Apply_and_IndexFor_round_trip(int index, LayoutMode layout, GridMode grid)
    {
        var settings = new AppSettings();
        LayoutChoices.Apply(settings, index);
        Assert.Equal(layout, settings.LayoutMode);
        if (layout == LayoutMode.Grid) Assert.Equal(grid, settings.GridMode);
        Assert.Equal(index, LayoutChoices.IndexFor(settings));
    }

    [Fact]
    public void Choosing_a_layout_keeps_the_grid_size_for_later()
    {
        var settings = new AppSettings { GridMode = GridMode.Nine };
        LayoutChoices.Apply(settings, 7);
        Assert.Equal(GridMode.Nine, settings.GridMode);
    }
}
```

`tests/Centinela.Desktop.Tests/LayoutControllerTests.cs`:

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Layout;

namespace Centinela.Desktop.Tests;

public class LayoutControllerTests
{
    static List<Camera> Cameras(int n) =>
        Enumerable.Range(0, n).Select(i => new Camera { Name = $"C{i}", Host = $"h{i}", Order = i }).ToList();

    [Fact]
    public void Grid_uses_substreams_and_no_big_tile()
    {
        var plan = new LayoutController(Cameras(4), new AppSettings()).Plan();
        Assert.Equal((2, 2), (plan.Rows, plan.Columns));
        Assert.All(plan.Tiles, t => Assert.Equal((StreamKind.Sub, false), (t.Stream, t.Big)));
    }

    [Fact]
    public void Featured_tile_is_big_on_the_main_stream_unless_low_quality()
    {
        var cams = Cameras(5);
        var settings = new AppSettings { LayoutMode = LayoutMode.Featured, FeaturedCameraId = cams[2].Id };
        var big = new LayoutController(cams, settings).Plan().Tiles.Single(t => t.Big);
        Assert.Equal((cams[2].Id, StreamKind.Main), (big.CameraId, big.Stream));

        cams[2].LowQualityWhenBig = true;
        big = new LayoutController(cams, settings).Plan().Tiles.Single(t => t.Big);
        Assert.Equal((cams[2].Id, StreamKind.Sub), (big.CameraId, big.Stream));
    }

    [Fact]
    public void Click_features_a_thumbnail_and_ignores_the_featured_one()
    {
        var cams = Cameras(4);
        var settings = new AppSettings { LayoutMode = LayoutMode.Featured };
        var layout = new LayoutController(cams, settings);
        Assert.False(layout.Click(cams[0].Id)); // first camera is featured by default
        Assert.True(layout.Click(cams[3].Id));
        Assert.Equal(cams[3].Id, settings.FeaturedCameraId);
    }

    [Fact]
    public void Click_in_dual_replaces_the_older_big_camera()
    {
        var cams = Cameras(5);
        var settings = new AppSettings { LayoutMode = LayoutMode.Dual };
        var layout = new LayoutController(cams, settings);
        Assert.True(layout.Click(cams[4].Id));
        Assert.Contains(cams[4].Id, layout.BigCameraIds());
        Assert.Equal(2, layout.BigCameraIds().Count);
    }

    [Fact]
    public void Click_in_the_grid_does_nothing()
    {
        var cams = Cameras(4);
        Assert.False(new LayoutController(cams, new AppSettings()).Click(cams[1].Id));
    }

    [Fact]
    public void Drop_on_another_tile_swaps_the_order()
    {
        var cams = Cameras(3);
        var layout = new LayoutController(cams, new AppSettings());
        Assert.Equal(DropOutcome.OrderChanged, layout.Drop(cams[0].Id, cams[2].Id));
        Assert.Equal((2, 0), (cams[0].Order, cams[2].Order));
    }

    [Fact]
    public void Drop_on_the_featured_tile_features_the_dragged_camera()
    {
        var cams = Cameras(3);
        var settings = new AppSettings { LayoutMode = LayoutMode.Featured };
        var layout = new LayoutController(cams, settings);
        Assert.Equal(DropOutcome.SettingsChanged, layout.Drop(cams[2].Id, cams[0].Id));
        Assert.Equal(cams[2].Id, settings.FeaturedCameraId);
        Assert.Equal([0, 1, 2], cams.Select(c => c.Order));
    }

    [Fact]
    public void Drop_on_a_big_dual_tile_puts_the_dragged_camera_there()
    {
        var cams = Cameras(4);
        var settings = new AppSettings { LayoutMode = LayoutMode.Dual };
        var layout = new LayoutController(cams, settings);
        var target = layout.BigCameraIds()[1];
        Assert.Equal(DropOutcome.SettingsChanged, layout.Drop(cams[3].Id, target));
        Assert.Equal(cams[3].Id, layout.BigCameraIds()[1]);
    }

    [Fact]
    public void Drop_with_an_unknown_camera_does_nothing()
    {
        var cams = Cameras(2);
        Assert.Equal(DropOutcome.None, new LayoutController(cams, new AppSettings()).Drop(Guid.NewGuid(), cams[0].Id));
    }
}
```

- [ ] **Step 2: Ejecutar para ver que fallan** (error de compilación).

- [ ] **Step 3: Implementar** — `Services/Layout/LayoutChoices.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Desktop.Services.Layout;

/// <summary>The «Vista» choices: five grid sizes, then the layouts, in this order.</summary>
static class LayoutChoices
{
    static readonly GridMode[] GridModes = [GridMode.Auto, GridMode.One, GridMode.Four, GridMode.Nine, GridMode.Sixteen];
    static readonly LayoutMode[] Layouts = [LayoutMode.Featured, LayoutMode.FeaturedLeft, LayoutMode.Dual];

    public static IReadOnlyList<string> Labels { get; } =
    [
        "Auto", "1", "4", "9", "16",
        "Principal + miniaturas (derecha)", "Principal + miniaturas (izquierda)", "Dos principales + miniaturas",
    ];

    public static int IndexFor(AppSettings settings) =>
        settings.LayoutMode == LayoutMode.Grid
            ? Math.Max(0, Array.IndexOf(GridModes, settings.GridMode))
            : GridModes.Length + Math.Max(0, Array.IndexOf(Layouts, settings.LayoutMode));

    public static void Apply(AppSettings settings, int index)
    {
        if (index >= GridModes.Length)
        {
            settings.LayoutMode = Layouts[Math.Clamp(index - GridModes.Length, 0, Layouts.Length - 1)];
            return;
        }
        settings.LayoutMode = LayoutMode.Grid;
        settings.GridMode = GridModes[Math.Max(0, index)];
    }
}
```

`Services/Layout/PlacedTile.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Desktop.Services.Layout;

/// <param name="Stream">The stream the tile plays (a big low-quality camera plays its substream).</param>
/// <param name="Big">A big tile of the featured/dual layouts: zoom, large buttons, quality button.</param>
readonly record struct PlacedTile(Guid CameraId, StreamKind Stream, bool Big, int Row, int Column, int RowSpan, int ColumnSpan);

sealed record LayoutPlan(int Rows, int Columns, IReadOnlyList<PlacedTile> Tiles);

enum DropOutcome { None, SettingsChanged, OrderChanged }
```

`Services/Layout/LayoutController.cs` (lógica de `MainWindow.View.cs`, `FeatureCamera` y `SwapCameras` de la WPF):

```csharp
using Centinela.Core;

namespace Centinela.Desktop.Services.Layout;

/// <summary>
/// Which tile goes where for the current cameras and settings, and what a click or a drop changes. Mutates the
/// settings (featured/dual choice) and the cameras' order; the caller saves and rebuilds the view.
/// </summary>
sealed class LayoutController(IReadOnlyList<Camera> cameras, AppSettings settings)
{
    public bool IsFeaturedLayout => settings.LayoutMode is LayoutMode.Featured or LayoutMode.FeaturedLeft;

    public LayoutPlan Plan()
    {
        var plan = PlanView();
        var bigs = plan.Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToHashSet();
        var tiles = plan.Slots.Select(s =>
        {
            var camera = cameras.First(c => c.Id == s.CameraId);
            return new PlacedTile(s.CameraId, ViewPlanner.StreamFor(camera, s.Kind), bigs.Contains(s.CameraId),
                s.Row, s.Column, s.RowSpan, s.ColumnSpan);
        }).ToList();
        return new LayoutPlan(plan.Rows, plan.Columns, tiles);
    }

    /// <summary>The cameras shown big, in plan order (dual: left, right).</summary>
    public IReadOnlyList<Guid> BigCameraIds() =>
        PlanView().Slots.Where(s => s.Kind == StreamKind.Main).Select(s => s.CameraId).ToList();

    /// <summary>A click on a tile: features it, or in dual replaces the older big camera. True when something changed.</summary>
    public bool Click(Guid cameraId)
    {
        if (IsFeaturedLayout)
        {
            if (FeaturedCameraId() == cameraId) return false;
            settings.FeaturedCameraId = cameraId;
            return true;
        }
        if (settings.LayoutMode != LayoutMode.Dual) return false;
        var current = new DualState(BigCameraIds(), settings.DualNextReplace);
        var next = DualSelection.Click(current, cameraId);
        if (ReferenceEquals(next, current)) return false;
        Store(next);
        return true;
    }

    /// <summary>A tile dragged onto another one.</summary>
    public DropOutcome Drop(Guid source, Guid target)
    {
        var a = cameras.FirstOrDefault(c => c.Id == source);
        var b = cameras.FirstOrDefault(c => c.Id == target);
        if (a is null || b is null || source == target) return DropOutcome.None;
        if (IsFeaturedLayout && target == FeaturedCameraId())
        {
            // Dropping a thumbnail onto the featured tile features the dragged camera.
            settings.FeaturedCameraId = source;
            return DropOutcome.SettingsChanged;
        }
        if (settings.LayoutMode == LayoutMode.Dual && BigCameraIds() is var bigs && bigs.Contains(target))
        {
            // Dropping onto a big camera puts the dragged camera there (swapping when both are big).
            Store(DualSelection.Drop(new DualState(bigs, settings.DualNextReplace), source, bigs.ToList().IndexOf(target)));
            return DropOutcome.SettingsChanged;
        }
        (a.Order, b.Order) = (b.Order, a.Order);
        return DropOutcome.OrderChanged;
    }

    ViewPlan PlanView() =>
        ViewPlanner.Plan(cameras, settings.LayoutMode, settings.GridMode, settings.FeaturedCameraId, settings.DualCameraIds);

    Guid? FeaturedCameraId() => IsFeaturedLayout && BigCameraIds() is [var first, ..] ? first : null;

    void Store(DualState state)
    {
        settings.DualCameraIds = [.. state.Ids];
        settings.DualNextReplace = state.NextReplace;
    }
}
```

- [ ] **Step 4: Verificar** — `dotnet test tests/Centinela.Desktop.Tests -nologo -v q` → todas pasan.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Desktop/Services/Layout/LayoutChoices.cs src/Centinela.Desktop/Services/Layout/PlacedTile.cs src/Centinela.Desktop/Services/Layout/LayoutController.cs tests/Centinela.Desktop.Tests/LayoutChoicesTests.cs tests/Centinela.Desktop.Tests/LayoutControllerTests.cs
git commit -m "feat(desktop): vistas y colocación de las cámaras"
```

---

### Task 4: Textos de estado y errores, sesiones compartidas y `TileViewModel`

**Files:**
- Create: `src/Centinela.Desktop/Services/Errors/ErrorText.cs`, `src/Centinela.Desktop/ViewModels/TileStatus.cs`, `src/Centinela.Desktop/Services/Streams/StreamLeases.cs`, `src/Centinela.Desktop/ViewModels/TileViewModel.cs`
- Create: `tests/Centinela.Desktop.Tests/TileStatusTests.cs`, `tests/Centinela.Desktop.Tests/ErrorTextTests.cs`

**Interfaces:**
- Consumes: `ErrorTranslator`, `TranslatedError`, `StreamErrorKind`, `SessionState`, `StreamSession`, `SharedStreams`, `SharedStreamLease`, `StreamUrlBuilder` (Core/Media); `UiThread` (Task 1).
- Produces:
  - `static class ErrorText { static TranslatedError Translate(Camera camera, StreamErrorKind kind); }` (igual que `ErrorCenter.Translate` de la WPF).
  - `static class TileStatus { static string Text(SessionState state, StreamErrorKind? error, Camera camera); }`.
  - `sealed class StreamLeases : IDisposable { SharedStreamLease? AcquireSub(Camera camera, int width, int height); IReadOnlyList<StreamSession> SessionsFor(Guid cameraId); event Action<StreamSession, Camera>? SessionCreated; }`.
  - `sealed partial class TileViewModel : ObservableObject` con: `Camera Camera`, `StreamKind Kind`, `bool Manage`, `StreamSession? Session`, observables `string Name`, `string StatusText`, `bool IsPlaying`, `bool Big`, `bool ShowStats`, `string StatsText`, `bool StatsStale`, `bool SmoothingVisible`, `string SmoothingToolTip`; métodos `Start()`, `SetTargetSize(int w, int h)`, `RefreshStats()`, `Task ShutdownAsync()`; eventos `StreamFailed`, `Playing`, `ErrorReported(TileViewModel, StreamError)`.

- [ ] **Step 1: Pruebas** — `tests/Centinela.Desktop.Tests/ErrorTextTests.cs`:

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Errors;

namespace Centinela.Desktop.Tests;

public class ErrorTextTests
{
    [Fact]
    public void Auth_failure_without_a_stored_password_says_it_is_missing()
    {
        var camera = new Camera { Name = "Garaje", Password = "" };
        var text = ErrorText.Translate(camera, StreamErrorKind.AuthFailed);
        Assert.Equal(("Falta contraseña", "Garaje: falta la contraseña"), (text.Short, text.Title));
    }

    [Fact]
    public void Other_errors_use_the_core_translation()
    {
        var camera = new Camera { Name = "Garaje", Password = "x", Brand = Brand.Tapo };
        Assert.Equal(ErrorTranslator.Translate(StreamErrorKind.Unreachable, Brand.Tapo, "Garaje"),
            ErrorText.Translate(camera, StreamErrorKind.Unreachable));
    }
}
```

`tests/Centinela.Desktop.Tests/TileStatusTests.cs` (mismo comportamiento que `CameraTile.ShowState/ShowErrorKind`):

```csharp
using Centinela.Core;
using Centinela.Desktop.ViewModels;
using Centinela.Media;

namespace Centinela.Desktop.Tests;

public class TileStatusTests
{
    static readonly Camera Cam = new() { Name = "Patio", Password = "x", Brand = Brand.Tapo };

    [Theory]
    [InlineData(SessionState.Connecting, "Conectando…")]
    [InlineData(SessionState.Reconnecting, "Reconectando…")]
    [InlineData(SessionState.Stopped, "Detenida")]
    [InlineData(SessionState.Playing, "")]
    [InlineData(SessionState.Idle, "")]
    public void States_without_an_error(SessionState state, string expected) =>
        Assert.Equal(expected, TileStatus.Text(state, null, Cam));

    [Fact]
    public void An_error_stays_on_screen_while_retrying()
    {
        var shortText = ErrorTranslator.Translate(StreamErrorKind.Unreachable, Brand.Tapo, "Patio").Short;
        Assert.Equal($"{shortText} · reintentando", TileStatus.Text(SessionState.Reconnecting, StreamErrorKind.Unreachable, Cam));
        Assert.Equal($"{shortText} · reintentando", TileStatus.Text(SessionState.Connecting, StreamErrorKind.Unreachable, Cam));
    }

    [Fact]
    public void Auth_failure_is_shown_without_retrying()
    {
        var shortText = ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Tapo, "Patio").Short;
        Assert.Equal(shortText, TileStatus.Text(SessionState.AuthFailed, StreamErrorKind.AuthFailed, Cam));
        Assert.Equal(shortText, TileStatus.Text(SessionState.AuthFailed, null, Cam));
    }

    [Fact]
    public void Playing_clears_any_error() =>
        Assert.Equal("", TileStatus.Text(SessionState.Playing, StreamErrorKind.Unreachable, Cam));
}
```

- [ ] **Step 2: Ejecutar para ver que fallan** (error de compilación).

- [ ] **Step 3: Implementar** — `Services/Errors/ErrorText.cs`:

```csharp
using Centinela.Core;

namespace Centinela.Desktop.Services.Errors;

static class ErrorText
{
    /// <summary>An auth failure on a camera with no stored password means the password is missing (e.g. a copy imported without passwords).</summary>
    public static TranslatedError Translate(Camera camera, StreamErrorKind kind) =>
        kind == StreamErrorKind.AuthFailed && camera.Password.Length == 0
            ? new TranslatedError("Falta contraseña", $"{camera.Name}: falta la contraseña", "Pulsa «Editar» en la cámara y escribe su contraseña.")
            : ErrorTranslator.Translate(kind, camera.Brand, camera.Name);
}
```

`ViewModels/TileStatus.cs`:

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Errors;
using Centinela.Media;

namespace Centinela.Desktop.ViewModels;

/// <summary>The text over a tile's video for its session state and last error (kept while retrying).</summary>
static class TileStatus
{
    public static string Text(SessionState state, StreamErrorKind? error, Camera camera)
    {
        if (state == SessionState.Playing) return "";
        if (state == SessionState.AuthFailed) return ErrorText.Translate(camera, StreamErrorKind.AuthFailed).Short;
        if (error is { } kind && state is SessionState.Connecting or SessionState.Reconnecting)
        {
            var text = ErrorText.Translate(camera, kind).Short;
            return kind == StreamErrorKind.AuthFailed ? text : $"{text} · reintentando";
        }
        return state switch
        {
            SessionState.Connecting => "Conectando…",
            SessionState.Reconnecting => "Reconectando…",
            SessionState.Stopped => "Detenida",
            _ => "",
        };
    }
}
```

`Services/Streams/StreamLeases.cs` (de `MainWindow.Streams.cs` de la WPF):

```csharp
using Centinela.Core;
using Centinela.Media;

namespace Centinela.Desktop.Services.Streams;

/// <summary>One shared substream connection per camera, whoever is watching it.</summary>
sealed class StreamLeases : IDisposable
{
    readonly SharedStreams _streams = new();
    bool _disposed;

    /// <summary>Raised once per new shared session, before it starts (error reporting hooks in, phase 2d).</summary>
    public event Action<StreamSession, Camera>? SessionCreated;

    /// <summary>A lease on the camera's shared substream session; null without a usable Sub URL or after Dispose.</summary>
    public SharedStreamLease? AcquireSub(Camera camera, int width, int height)
    {
        if (_disposed) return null;
        string url;
        try
        {
            url = StreamUrlBuilder.Build(camera, StreamKind.Sub);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        try
        {
            return _streams.Acquire(camera.Id, url, camera.UseUdp, width, height, session =>
            {
                session.Smoothing = camera.Smoothing;
                SessionCreated?.Invoke(session, camera);
            });
        }
        catch (ObjectDisposedException)
        {
            return null; // closing
        }
    }

    public IReadOnlyList<StreamSession> SessionsFor(Guid cameraId) => _streams.SessionsFor(cameraId);

    public void Dispose()
    {
        _disposed = true;
        _streams.Dispose();
    }
}
```

`ViewModels/TileViewModel.cs` (la parte sin interfaz de `CameraTile.xaml.cs`: sesión propia o arrendada, estado, estadísticas y suavizado; zoom, audio, botones y grabación llegan en 2b):

```csharp
using System.Globalization;
using Centinela.Core;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.Services.Ui;
using Centinela.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>
/// One camera tile: its live session (own for the main stream, a shared lease for the substream), the status text
/// and the stats. Session events arrive on session threads and are posted to the UI thread.
/// </summary>
sealed partial class TileViewModel : ObservableObject
{
    static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    readonly SharedStreamLease? _lease;
    readonly Action<SessionState>? _onState;
    readonly Action<StreamError>? _onError;
    readonly Action<double>? _onSmoothing;
    StreamErrorKind? _errorKind;
    Task? _shutdown;
    bool _disposed;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _big;
    [ObservableProperty] private bool _showStats;
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private bool _statsStale;
    [ObservableProperty] private bool _smoothingVisible;
    [ObservableProperty] private string _smoothingToolTip = "";

    /// <param name="manage">False for fullscreen tiles: no drag, no editing.</param>
    public TileViewModel(Camera camera, StreamKind kind, bool manage, StreamLeases? leases)
    {
        Camera = camera;
        Kind = kind;
        Manage = manage;
        _name = camera.Name;
        if (kind == StreamKind.Sub && leases is not null)
        {
            _lease = leases.AcquireSub(camera, 0, 0);
            Session = _lease?.Session;
        }
        else
        {
            try
            {
                Session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp);
                Session.Smoothing = camera.Smoothing;
            }
            catch (InvalidOperationException)
            {
                Session = null;
            }
        }
        if (Session is null)
        {
            StatusText = "Falta la URL RTSP";
            return;
        }
        _onState = state => UiThread.Post(() => ShowState(state));
        _onError = error => UiThread.Post(() => ShowError(error));
        _onSmoothing = seconds => UiThread.Post(() => ShowSmoothing(seconds));
        Session.StateChanged += _onState;
        Session.ErrorOccurred += _onError;
        Session.SmoothingChanged += _onSmoothing;
        ShowSmoothing(Session.SmoothingSeconds);
        StatusText = "Conectando…";
    }

    public Camera Camera { get; }
    public StreamKind Kind { get; }
    public bool Manage { get; }
    /// <summary>Null when the camera has no usable URL: the tile only shows the error.</summary>
    public StreamSession? Session { get; }
    /// <summary>True when the session is shared (substream lease); the window reports its errors once.</summary>
    public bool IsShared => _lease is not null;

    /// <summary>Raised on the UI thread each time the stream reports reconnecting or failed authentication.</summary>
    public event Action<TileViewModel>? StreamFailed;
    /// <summary>Raised on the UI thread each time an own (not shared) stream reaches Playing.</summary>
    public event Action<TileViewModel>? Playing;
    /// <summary>Raised on the UI thread for each error of an own stream (shared ones are reported by the window).</summary>
    public event Action<TileViewModel, StreamError>? ErrorReported;

    /// <summary>Starts an own session; a shared one shows where it already is.</summary>
    public void Start()
    {
        if (Session is not { } session) return;
        if (_lease is null)
        {
            session.Start();
            return;
        }
        var current = session.State;
        if (current != SessionState.Playing && session.LastErrorKind is { } lastError) _errorKind = lastError;
        ShowState(current == SessionState.Idle ? SessionState.Connecting : current);
    }

    /// <summary>The decode box in device pixels; ignored until the tile has a real size.</summary>
    public void SetTargetSize(int width, int height)
    {
        if (width < 1 || height < 1) return;
        if (_lease is not null) _lease.SetTargetSize(width, height);
        else Session?.SetTargetSize(width, height);
    }

    partial void OnShowStatsChanged(bool value)
    {
        if (value) RefreshStats();
    }

    /// <summary>fps · ms · GPU/CPU (and smoothing); called twice a second by the view while stats are shown.</summary>
    public void RefreshStats()
    {
        if (Session is null || _disposed) return;
        if (Session.State != SessionState.Playing)
        {
            // Latency and decoder describe the last stream that played; not meaningful until it plays again.
            StatsText = "— fps";
            StatsStale = true;
            return;
        }
        var s = Session.Stats;
        StatsText = $"{s.Fps:0} fps · {s.LatencyMs:0} ms · {(s.HardwareDecoding ? "GPU" : "CPU")}"
            + (s.SmoothingSeconds > 0
                ? string.Create(CultureInfo.InvariantCulture, $" · suav. {s.SmoothingSeconds:0.##} s · cortes {s.Rebuffers}")
                : "");
        StatsStale = s.SinceLastFrame > TimeSpan.FromSeconds(1);
    }

    void ShowState(SessionState state)
    {
        if (_disposed) return;
        if (state == SessionState.AuthFailed) _errorKind = StreamErrorKind.AuthFailed;
        StatusText = TileStatus.Text(state, _errorKind, Camera);
        IsPlaying = state == SessionState.Playing;
        if (state is SessionState.Reconnecting or SessionState.AuthFailed) StreamFailed?.Invoke(this);
        if (state == SessionState.Playing)
        {
            _errorKind = null;
            if (_lease is null) Playing?.Invoke(this);
        }
    }

    void ShowError(StreamError error)
    {
        if (_disposed) return;
        _errorKind = error.Kind;
        StatusText = TileStatus.Text(Session?.State ?? SessionState.Reconnecting, _errorKind, Camera);
        if (_lease is null) ErrorReported?.Invoke(this, error);
    }

    void ShowSmoothing(double seconds)
    {
        if (_disposed) return;
        SmoothingVisible = seconds > 0;
        SmoothingToolTip = string.Create(Spanish, $"Imagen suavizada: {seconds:0.##} s de retraso para que se vea sin tirones.");
    }

    /// <summary>
    /// Stops the live view (own session disposed off the UI thread) or releases the shared lease. UI thread;
    /// repeated calls return the same task.
    /// </summary>
    public Task ShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        _disposed = true;
        if (Session is { } session)
        {
            session.StateChanged -= _onState;
            session.ErrorOccurred -= _onError;
            session.SmoothingChanged -= _onSmoothing;
        }
        if (_lease is not null) return _shutdown = _lease.ReleaseAsync();
        Session?.RequestStop();
        return _shutdown = Session is null ? Task.CompletedTask : Task.Run(Session.Dispose);
    }
}
```

Nota: en `ShowError`, la WPF mostraba el error aunque el estado siguiera en Connecting; `TileStatus.Text` con `Session.State` produce el mismo texto («… · reintentando», o solo el corto para AuthFailed).

- [ ] **Step 4: Verificar** — `dotnet test tests/Centinela.Desktop.Tests -nologo -v q` → todas pasan; `dotnet build src/Centinela.Desktop -p:BaseOutputPath=<scratch>/bin/ -nologo -v q` → 0 advertencias.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Desktop/Services/Errors/ErrorText.cs src/Centinela.Desktop/ViewModels/TileStatus.cs src/Centinela.Desktop/Services/Streams/StreamLeases.cs src/Centinela.Desktop/ViewModels/TileViewModel.cs tests/Centinela.Desktop.Tests/TileStatusTests.cs tests/Centinela.Desktop.Tests/ErrorTextTests.cs
git commit -m "feat(desktop): estado de cada cámara y conexiones compartidas"
```

---

### Task 5: `VideoSurface` y `CameraTileView`

**Files:**
- Create: `src/Centinela.Desktop/Video/VideoSurface.cs`, `src/Centinela.Desktop/Views/CameraTileView.axaml`, `src/Centinela.Desktop/Views/CameraTileView.axaml.cs`

**Interfaces:**
- Consumes: `TileViewModel` (Task 4), `FrameMailbox`/`VideoFrame` (Media).
- Produces:
  - `sealed class VideoSurface : Control { StreamSession? Session { get; set; } event Action? FirstFrameShown; Rect ContentRect { get; } }` — pinta el último fotograma (sin cola), escalado uniforme y centrado.
  - `partial class CameraTileView : UserControl` con eventos `Clicked(CameraTileView)`, `FullscreenRequested(CameraTileView)`, `SwapRequested(Guid source, Guid target)`, `FirstFrameShown(CameraTileView)`; propiedad `TileViewModel ViewModel`; método `SetDropHover(bool)` interno.

- [ ] **Step 1: `Video/VideoSurface.cs`**

```csharp
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Centinela.Media;

namespace Centinela.Desktop.Video;

/// <summary>
/// Paints the newest frame of a session's mailbox on every screen refresh: latest wins, nothing is queued, so no
/// latency accumulates (same rule as the Windows version). Letterboxed, centred.
/// </summary>
sealed class VideoSurface : Control
{
    WriteableBitmap? _bitmap;
    long _sequence;
    bool _attached;
    bool _firstFrame;

    public StreamSession? Session { get; set; }

    /// <summary>Raised once, after the first frame has been painted.</summary>
    public event Action? FirstFrameShown;

    /// <summary>Where the image sits inside the control (letterboxed).</summary>
    public Rect ContentRect { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    void RequestFrame()
    {
        if (_attached) TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => OnFrame());
    }

    void OnFrame()
    {
        if (Session is { } session && session.Mailbox.TryRead(ref _sequence, CopyFrame))
        {
            InvalidateVisual();
            if (!_firstFrame)
            {
                _firstFrame = true;
                FirstFrameShown?.Invoke();
            }
        }
        RequestFrame();
    }

    void CopyFrame(VideoFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using var target = _bitmap.Lock();
        if (target.RowBytes == frame.Stride)
        {
            Marshal.Copy(frame.Data, 0, target.Address, frame.Stride * frame.Height);
            return;
        }
        for (var row = 0; row < frame.Height; row++)
            Marshal.Copy(frame.Data, row * frame.Stride, target.Address + row * target.RowBytes, frame.Stride);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is not { } bitmap) return;
        var source = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var size = new Size(source.Width * scale, source.Height * scale);
        ContentRect = new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
        context.DrawImage(bitmap, source, ContentRect);
    }
}
```

- [ ] **Step 2: `Views/CameraTileView.axaml`** (mismo aspecto que `CameraTile.xaml`: nombre arriba a la izquierda, estado centrado, estadísticas abajo a la izquierda; los botones llegan en 2b)

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Centinela.Desktop.ViewModels"
             xmlns:video="using:Centinela.Desktop.Video"
             x:Class="Centinela.Desktop.Views.CameraTileView"
             x:DataType="vm:TileViewModel"
             Background="Black" DragDrop.AllowDrop="True">
    <Border x:Name="Frame" BorderBrush="#2A2A2A" BorderThickness="1">
        <Grid>
            <video:VideoSurface x:Name="Video" Opacity="{Binding IsPlaying, Converter={x:Static video:VideoOpacity.Converter}}" />
            <TextBlock Text="{Binding StatusText}" Foreground="#DDDDDD" FontSize="14" TextWrapping="Wrap"
                       TextAlignment="Center" HorizontalAlignment="Center" VerticalAlignment="Center" />
            <Border Background="#80000000" Padding="6,2" CornerRadius="0,0,4,0"
                    HorizontalAlignment="Left" VerticalAlignment="Top">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{Binding Name}" Foreground="White" />
                    <TextBlock Text=" ⏱" Foreground="White" IsVisible="{Binding SmoothingVisible}"
                               ToolTip.Tip="{Binding SmoothingToolTip}" />
                </StackPanel>
            </Border>
            <Border Background="#80000000" Padding="6,1" CornerRadius="0,4,0,0"
                    HorizontalAlignment="Left" VerticalAlignment="Bottom" IsVisible="{Binding ShowStats}">
                <TextBlock Text="{Binding StatsText}" FontSize="11" FontFamily="Consolas,Menlo,monospace"
                           Foreground="{Binding StatsStale, Converter={x:Static video:StatsBrush.Converter}}" />
            </Border>
        </Grid>
    </Border>
</UserControl>
```

Añadir a `Video/VideoSurface.cs` (mismo archivo, al final, porque solo sirven a esta vista) los dos convertidores:

```csharp
/// <summary>Dimmed video while not playing (as in the Windows version).</summary>
static class VideoOpacity
{
    public static readonly Avalonia.Data.Converters.IValueConverter Converter =
        new Avalonia.Data.Converters.FuncValueConverter<bool, double>(playing => playing ? 1 : 0.4);
}

/// <summary>Stats in orange when the stream is not delivering frames.</summary>
static class StatsBrush
{
    public static readonly Avalonia.Data.Converters.IValueConverter Converter =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(stale => stale ? Brushes.Orange : Brushes.White);
}
```

- [ ] **Step 3: `Views/CameraTileView.axaml.cs`** (clic simple retrasado el tiempo de doble clic, doble clic a pantalla completa, arrastrar para reordenar, borde de destino, tamaño de decodificación, estadísticas cada 500 ms)

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

partial class CameraTileView : UserControl
{
    const string DragFormat = "Centinela.CameraId";
    static readonly IBrush NormalBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    static readonly IBrush DropBorder = Brushes.DodgerBlue;
    readonly DispatcherTimer _clickTimer = new();
    readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    Point? _pressAt;
    PointerPressedEventArgs? _pressEvent;

    public CameraTileView() => InitializeComponent();

    public CameraTileView(TileViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Video.Session = viewModel.Session;
        Video.FirstFrameShown += () => FirstFrameShown?.Invoke(this);
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            Clicked?.Invoke(this);
        };
        _statsTimer.Tick += (_, _) => viewModel.RefreshStats();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TileViewModel.ShowStats)) UpdateStatsTimer();
        };
        UpdateStatsTimer();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropHover(false));
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SizeChanged += (_, _) => UpdateTargetSize();
    }

    public TileViewModel ViewModel { get; } = null!;

    /// <summary>A single click that did not start a drag, raised once the double-click time has passed.</summary>
    public event Action<CameraTileView>? Clicked;
    public event Action<CameraTileView>? FullscreenRequested;
    public event Action<Guid, Guid>? SwapRequested;
    /// <summary>Raised once, after the first video frame has been painted.</summary>
    public event Action<CameraTileView>? FirstFrameShown;

    void UpdateStatsTimer()
    {
        if (ViewModel.ShowStats) _statsTimer.Start();
        else _statsTimer.Stop();
    }

    void UpdateTargetSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        ViewModel.SetTargetSize((int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            _pressAt = null;
            FullscreenRequested?.Invoke(this);
            e.Handled = true;
            return;
        }
        _pressAt = e.GetPosition(this);
        _pressEvent = e;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // _pressAt is cleared when a drag starts or on double-click, so reaching here with it set means a plain click.
        if (_pressAt is null) return;
        _pressAt = null;
        _clickTimer.Interval = TopLevel.GetTopLevel(this)?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse)
            ?? TimeSpan.FromMilliseconds(500);
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // A press released outside the tile is not a click.
        _pressAt = null;
    }

    protected override async void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ViewModel.Manage || _pressAt is not { } start || _pressEvent is not { } press) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;
        _pressAt = null;
        _clickTimer.Stop();
        var data = new DataObject();
        data.Set(DragFormat, ViewModel.Camera.Id.ToString());
        await DragDrop.DoDragDrop(press, data, DragDropEffects.Move);
    }

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (ViewModel.Manage && e.Data.Contains(DragFormat)) SetDropHover(true);
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ViewModel.Manage && e.Data.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropHover(false);
        if (ViewModel.Manage && e.Data.Get(DragFormat) is string raw && Guid.TryParse(raw, out var source)
            && source != ViewModel.Camera.Id)
            SwapRequested?.Invoke(source, ViewModel.Camera.Id);
    }

    internal void SetDropHover(bool hover)
    {
        Frame.BorderBrush = hover ? DropBorder : NormalBorder;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _clickTimer.Stop();
        _statsTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
```

(Umbral de arrastre: 4 px, el mínimo de Windows por defecto, igual en todos los sistemas.)

- [ ] **Step 4: Compilar** — `dotnet build src/Centinela.Desktop -p:BaseOutputPath=<scratch>/bin/ -nologo -v q` → 0 advertencias, 0 errores. (Si alguna API de Avalonia 11.3 difiere de la escrita aquí —por ejemplo el nombre de `GetDoubleTapTime` o de `DataObject`—, usar la equivalente de 11.3 y anotarlo en el informe.)

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Desktop/Video/VideoSurface.cs src/Centinela.Desktop/Views/CameraTileView.axaml src/Centinela.Desktop/Views/CameraTileView.axaml.cs
git commit -m "feat(desktop): mosaico de cámara con vídeo, clic, doble clic y arrastrar"
```

---

### Task 6: `TileHost` y `TileFactory`: colocar mosaicos y relevo sin pantallazo negro

**Files:**
- Create: `src/Centinela.Desktop/Services/Tiles/TileFactory.cs`, `src/Centinela.Desktop/Services/Tiles/TileHost.cs`

**Interfaces:**
- Consumes: `LayoutPlan`/`PlacedTile` (Task 3), `TileViewModel` (Task 4), `CameraTileView` (Task 5), `StreamLeases`.
- Produces:
  - `sealed class TileFactory(StreamLeases leases, Func<bool> showStats) { event Action<CameraTileView>? Created; CameraTileView Create(Camera camera, StreamKind stream, bool big); }` — `Created` permite al dueño enganchar eventos (clic, soltar, pantalla completa, errores).
  - `sealed class TileHost(Grid grid, TileFactory factory, Func<Guid, Camera?> findCamera) { void Apply(LayoutPlan plan); IEnumerable<CameraTileView> All; IEnumerable<CameraTileView> TilesOf(Guid cameraId); void DisposeTilesOf(Guid cameraId); IReadOnlyList<Task> ShutdownAll(); event Action<Task, string>? ShutdownStarted; }`.

- [ ] **Step 1: `Services/Tiles/TileFactory.cs`**

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.ViewModels;
using Centinela.Desktop.Views;

namespace Centinela.Desktop.Services.Tiles;

/// <summary>Builds a tile (view model + view) for a camera and stream, started and ready to place.</summary>
sealed class TileFactory(StreamLeases leases, Func<bool> showStats)
{
    /// <summary>Raised for every new tile, before it starts, so the owner can wire its events.</summary>
    public event Action<CameraTileView>? Created;

    public CameraTileView Create(Camera camera, StreamKind stream, bool big)
    {
        var viewModel = new TileViewModel(camera, stream, manage: true, leases) { Big = big, ShowStats = showStats() };
        var view = new CameraTileView(viewModel);
        Created?.Invoke(view);
        viewModel.Start();
        return view;
    }
}
```

- [ ] **Step 2: `Services/Tiles/TileHost.cs`** (puerto de `RebuildView/PlaceTile/Cover/RetirePlaceholder/DisposeTile` de `MainWindow.View.cs`)

```csharp
using Avalonia.Controls;
using Avalonia.Threading;
using Centinela.Core;
using Centinela.Desktop.Services.Layout;
using Centinela.Desktop.Views;

namespace Centinela.Desktop.Services.Tiles;

/// <summary>
/// Puts the planned tiles in the grid: reuses a tile that already shows the same camera and stream, creates the
/// missing ones and retires the rest. When a camera switches stream (promoted/demoted), the old tile stays on top
/// of the new one until the new one paints, fails or 5 s pass, so the switch never flashes black. UI thread only.
/// </summary>
sealed class TileHost(Grid grid, TileFactory factory, Func<Guid, Camera?> findCamera)
{
    static readonly TimeSpan PlaceholderTimeout = TimeSpan.FromSeconds(5);
    readonly Dictionary<(Guid Id, StreamKind Kind), CameraTileView> _tiles = [];
    readonly Dictionary<CameraTileView, CameraTileView> _placeholders = []; // new tile → placeholder

    /// <summary>A tile's shutdown began (task, camera name); the owner waits for these on exit.</summary>
    public event Action<Task, string>? ShutdownStarted;

    public IEnumerable<CameraTileView> All => _tiles.Values.Concat(_placeholders.Values);

    public IEnumerable<CameraTileView> TilesOf(Guid cameraId) => All.Where(t => t.ViewModel.Camera.Id == cameraId);

    public void Apply(LayoutPlan plan)
    {
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        for (var r = 0; r < plan.Rows; r++) grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        for (var c = 0; c < plan.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var wanted = plan.Tiles.Select(t => (t.CameraId, t.Stream)).ToHashSet();
        foreach (var tile in plan.Tiles) Place(tile, wanted);
        foreach (var key in _tiles.Keys.Where(k => !wanted.Contains(k)).ToList()) DisposeTile(key);
    }

    void Place(PlacedTile slot, HashSet<(Guid, StreamKind)> wanted)
    {
        var key = (slot.CameraId, slot.Stream);
        var otherKey = (slot.CameraId, slot.Stream == StreamKind.Main ? StreamKind.Sub : StreamKind.Main);
        if (_tiles.TryGetValue(key, out var existing))
        {
            existing.ViewModel.Big = slot.Big;
            Position(existing, slot, z: 0);
            if (_placeholders.TryGetValue(existing, out var covering)) Position(covering, slot, z: 1);
            return;
        }
        var otherWanted = wanted.Contains(otherKey);

        // Swapped back before the other stream painted: the tile still on screen is this very stream.
        if (!otherWanted && _tiles.TryGetValue(otherKey, out var unpainted)
            && _placeholders.TryGetValue(unpainted, out var previous) && previous.ViewModel.Kind == slot.Stream)
        {
            _placeholders.Remove(unpainted);
            _tiles.Remove(otherKey);
            RemoveAndShutdown(unpainted);
            _tiles[key] = previous;
            previous.ViewModel.Big = slot.Big;
            Position(previous, slot, z: 0);
            return;
        }

        if (findCamera(slot.CameraId) is not { } camera) return;
        var tile = factory.Create(camera, slot.Stream, slot.Big);
        _tiles[key] = tile;
        Position(tile, slot, z: 0);
        grid.Children.Add(tile);

        // Same camera already on screen with the other stream: keep it over the new tile until that one has a frame.
        if (!otherWanted && _tiles.Remove(otherKey, out var placeholder))
        {
            if (tile.ViewModel.Session is null)
            {
                RemoveAndShutdown(placeholder);
                return;
            }
            Position(placeholder, slot, z: 1);
            _placeholders[tile] = placeholder;
            tile.FirstFrameShown += Retire;
            tile.ViewModel.StreamFailed += _ => Retire(tile);
            var timeout = new DispatcherTimer { Interval = PlaceholderTimeout };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                Retire(tile);
            };
            timeout.Start();
        }
    }

    void Retire(CameraTileView tile)
    {
        if (_placeholders.Remove(tile, out var placeholder)) RemoveAndShutdown(placeholder);
    }

    static void Position(Control tile, PlacedTile slot, int z)
    {
        Grid.SetRow(tile, slot.Row);
        Grid.SetColumn(tile, slot.Column);
        Grid.SetRowSpan(tile, slot.RowSpan);
        Grid.SetColumnSpan(tile, slot.ColumnSpan);
        tile.ZIndex = z;
    }

    void DisposeTile((Guid, StreamKind) key)
    {
        if (!_tiles.Remove(key, out var tile)) return;
        if (_placeholders.Remove(tile, out var placeholder)) RemoveAndShutdown(placeholder);
        RemoveAndShutdown(tile);
    }

    /// <summary>Removes every tile of the camera (edited or deleted camera).</summary>
    public void DisposeTilesOf(Guid cameraId)
    {
        foreach (var key in _tiles.Keys.Where(k => k.Id == cameraId).ToList()) DisposeTile(key);
    }

    void RemoveAndShutdown(CameraTileView tile)
    {
        grid.Children.Remove(tile);
        ShutdownStarted?.Invoke(tile.ViewModel.ShutdownAsync(), tile.ViewModel.Camera.Name);
    }

    /// <summary>Stops every tile (exit); returns their shutdowns.</summary>
    public IReadOnlyList<Task> ShutdownAll()
    {
        var shutdowns = All.Select(t => t.ViewModel.ShutdownAsync()).ToList();
        _tiles.Clear();
        _placeholders.Clear();
        grid.Children.Clear();
        return shutdowns;
    }
}
```

- [ ] **Step 3: Compilar** → 0 advertencias, 0 errores.

- [ ] **Step 4: Commit**

```bash
git add src/Centinela.Desktop/Services/Tiles/TileFactory.cs src/Centinela.Desktop/Services/Tiles/TileHost.cs
git commit -m "feat(desktop): colocación de mosaicos y relevo sin pantallazo negro"
```

---

### Task 7: Ventana principal, pantalla completa y posición de la ventana

**Files:**
- Create: `src/Centinela.Desktop/Services/Ui/WindowPlacement.cs`, `src/Centinela.Desktop/ViewModels/MainViewModel.cs`, `src/Centinela.Desktop/Views/FullscreenWindow.axaml`, `src/Centinela.Desktop/Views/FullscreenWindow.axaml.cs`
- Modify: `src/Centinela.Desktop/Views/MainWindow.axaml`, `src/Centinela.Desktop/Views/MainWindow.axaml.cs`
- Create: `tests/Centinela.Desktop.Tests/WindowPlacementTests.cs`

**Interfaces:**
- Consumes: Tasks 2–6.
- Produces:
  - `static class WindowPlacement { static bool IsVisibleOn(IEnumerable<(int X, int Y, int Width, int Height)> screens, double left, double top); }` — true si la esquina está dentro de alguna pantalla con al menos 100 px de margen (misma regla que la WPF).
  - `sealed partial class MainViewModel : ObservableObject` con `CameraRepository Cameras`, `SettingsRepository Settings`, `LayoutController Layout`, observables `int LayoutIndex`, `bool ShowStats`, `string StatusText`, `bool IsEmpty`; `event Action? ViewChanged` (la ventana reconstruye la rejilla).
  - `MainWindow` completa; `FullscreenWindow(Camera camera, bool showStats)`.

- [ ] **Step 1: Prueba** — `tests/Centinela.Desktop.Tests/WindowPlacementTests.cs`

```csharp
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Tests;

public class WindowPlacementTests
{
    static readonly (int, int, int, int)[] TwoScreens = [(0, 0, 1920, 1080), (1920, 0, 1920, 1080)];

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(2500, 300, true)]      // second screen
    [InlineData(3800, 300, false)]     // less than 100 px left on the right
    [InlineData(-50, 100, false)]
    [InlineData(100, 1000, false)]     // less than 100 px left at the bottom
    public void A_saved_corner_must_leave_the_window_reachable(double left, double top, bool expected) =>
        Assert.Equal(expected, WindowPlacement.IsVisibleOn(TwoScreens, left, top));
}
```

- [ ] **Step 2: Ejecutar para ver que falla**, luego implementar `Services/Ui/WindowPlacement.cs`:

```csharp
namespace Centinela.Desktop.Services.Ui;

/// <summary>Whether a saved window corner is still on a connected screen (a monitor may have been unplugged).</summary>
static class WindowPlacement
{
    const int Margin = 100;

    public static bool IsVisibleOn(IEnumerable<(int X, int Y, int Width, int Height)> screens, double left, double top) =>
        screens.Any(s => left >= s.X && top >= s.Y && left < s.X + s.Width - Margin && top < s.Y + s.Height - Margin);
}
```

- [ ] **Step 3: `ViewModels/MainViewModel.cs`**

```csharp
using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Layout;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Centinela.Desktop.ViewModels;

/// <summary>The main window's state: cameras, settings, chosen layout, stats and the status line.</summary>
sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _layoutIndex;
    [ObservableProperty] private bool _showStats;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isEmpty;

    public MainViewModel(CameraRepository cameras, SettingsRepository settings)
    {
        Cameras = cameras;
        Settings = settings;
        Layout = new LayoutController(cameras.All, settings.Settings);
        _layoutIndex = LayoutChoices.IndexFor(settings.Settings);
        _showStats = settings.Settings.ShowStats;
        _isEmpty = cameras.All.Count == 0;
        cameras.SaveFailed += message => StatusText = message;
    }

    public CameraRepository Cameras { get; }
    public SettingsRepository Settings { get; }
    public LayoutController Layout { get; }
    public IReadOnlyList<string> LayoutLabels => LayoutChoices.Labels;

    /// <summary>The grid must be rebuilt (layout, order or featured camera changed).</summary>
    public event Action? ViewChanged;

    partial void OnLayoutIndexChanged(int value)
    {
        LayoutChoices.Apply(Settings.Settings, value);
        Settings.Save();
        ViewChanged?.Invoke();
    }

    partial void OnShowStatsChanged(bool value)
    {
        Settings.Settings.ShowStats = value;
        Settings.Save();
    }

    public void Click(Guid cameraId)
    {
        if (!Layout.Click(cameraId)) return;
        Settings.Save();
        ViewChanged?.Invoke();
    }

    public void Drop(Guid source, Guid target)
    {
        switch (Layout.Drop(source, target))
        {
            case DropOutcome.SettingsChanged:
                Settings.Save();
                break;
            case DropOutcome.OrderChanged:
                Cameras.Save();
                break;
            default:
                return;
        }
        ViewChanged?.Invoke();
    }

    /// <summary>Call after the cameras changed (count may have).</summary>
    public void CamerasChanged()
    {
        IsEmpty = Cameras.All.Count == 0;
        ViewChanged?.Invoke();
    }
}
```

- [ ] **Step 4: `Views/MainWindow.axaml`** (menú y barra como la WPF; en 2a solo están activos Salir, Mostrar estadísticas y Vista; los demás se añaden en 2b–2d)

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Centinela.Desktop.ViewModels"
        x:Class="Centinela.Desktop.Views.MainWindow"
        x:DataType="vm:MainViewModel"
        Title="Centinela" Width="1280" Height="800" Background="#111111"
        Icon="/Assets/centinela.ico">
    <DockPanel>
        <Menu x:Name="MainMenu" DockPanel.Dock="Top">
            <MenuItem Header="_Archivo">
                <MenuItem Header="_Salir" Click="Exit_Click" />
            </MenuItem>
            <MenuItem Header="_Ver">
                <MenuItem Header="Mostrar _estadísticas" ToggleType="CheckBox" IsChecked="{Binding ShowStats}"
                          Click="ShowStats_Click" />
            </MenuItem>
        </Menu>
        <StackPanel x:Name="TopBar" DockPanel.Dock="Top" Orientation="Horizontal" Margin="6,4" Spacing="6">
            <TextBlock Text="Vista:" VerticalAlignment="Center" />
            <ComboBox MinWidth="240" ItemsSource="{Binding LayoutLabels}" SelectedIndex="{Binding LayoutIndex}" />
        </StackPanel>
        <Border x:Name="BottomBar" DockPanel.Dock="Bottom" Padding="8,3" Background="#1E1E1E">
            <TextBlock Text="{Binding StatusText}" />
        </Border>
        <Grid>
            <Grid x:Name="TileGrid" />
            <TextBlock Text="No hay cámaras" Foreground="#AAAAAA" FontSize="20" IsVisible="{Binding IsEmpty}"
                       HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 5: `Views/MainWindow.axaml.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Desktop.Services.Data;
using Centinela.Desktop.Services.Streams;
using Centinela.Desktop.Services.Tiles;
using Centinela.Desktop.Services.Ui;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    readonly StreamLeases _leases = new();
    readonly TileHost _tiles;
    // Shutdowns of tiles removed from the grid; closing waits for them too (UI thread only).
    readonly List<Task> _pendingShutdowns = [];
    WindowState _stateBeforeFullscreen = WindowState.Normal;
    bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        var cameras = new CameraRepository(new CameraStore(CameraStore.DefaultPath, App.Platform.Secrets));
        cameras.Load();
        _vm = new MainViewModel(cameras, new SettingsRepository(new SettingsStore(SettingsStore.DefaultPath)));
        DataContext = _vm;
        var factory = new TileFactory(_leases, () => _vm.ShowStats);
        factory.Created += Wire;
        _tiles = new TileHost(TileGrid, factory, cameras.Find);
        _tiles.ShutdownStarted += Track;
        _vm.ViewChanged += Rebuild;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowStats))
                foreach (var tile in _tiles.All) tile.ViewModel.ShowStats = _vm.ShowStats;
        };
        RestorePlacement();
        Rebuild();
    }

    void Rebuild()
    {
        if (!_closing) _tiles.Apply(_vm.Layout.Plan());
    }

    void Wire(CameraTileView tile)
    {
        tile.Clicked += t => _vm.Click(t.ViewModel.Camera.Id);
        tile.SwapRequested += _vm.Drop;
        tile.FullscreenRequested += t => ShowFullscreen(t.ViewModel.Camera);
    }

    void ShowFullscreen(Camera shown)
    {
        var window = new FullscreenWindow(_vm.Cameras.CurrentOr(shown), _vm.ShowStats);
        window.Closed += (_, _) => Track(window.ShutdownAsync(), shown.Name);
        window.Show(this);
    }

    /// <summary>Another launch asked for the window: show it on top.</summary>
    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void Track(Task shutdown, string name)
    {
        _pendingShutdowns.Add(shutdown);
        shutdown.ContinueWith(done => UiThread.Post(() =>
        {
            _pendingShutdowns.Remove(shutdown);
            if (done.Exception?.GetBaseException().Message is { } error && !_closing)
                _vm.StatusText = $"Error al detener la cámara {name}: {error}";
        }), TaskScheduler.Default);
    }

    void ShowStats_Click(object? sender, RoutedEventArgs e) { /* the binding toggles ShowStats */ }

    void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen) ToggleFullscreen();
    }

    void ToggleFullscreen()
    {
        var entering = WindowState != WindowState.FullScreen;
        if (entering) _stateBeforeFullscreen = WindowState;
        MainMenu.IsVisible = TopBar.IsVisible = BottomBar.IsVisible = !entering;
        WindowState = entering ? WindowState.FullScreen : _stateBeforeFullscreen;
    }

    void RestorePlacement()
    {
        var s = _vm.Settings.Settings;
        Width = s.Width;
        Height = s.Height;
        var screens = Screens.All.Select(sc => (sc.Bounds.X, sc.Bounds.Y, sc.Bounds.Width, sc.Bounds.Height));
        if (s.Left is { } left && s.Top is { } top && WindowPlacement.IsVisibleOn(screens, left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)left, (int)top);
        }
        if (s.Maximized) WindowState = WindowState.Maximized;
    }

    void SavePlacement()
    {
        if (WindowState == WindowState.FullScreen) ToggleFullscreen();
        var s = _vm.Settings.Settings;
        s.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            s.Left = Position.X;
            s.Top = Position.Y;
            s.Width = Width;
            s.Height = Height;
        }
        _vm.Settings.Save();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SavePlacement();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        foreach (var owned in OwnedWindows.ToList()) owned.Close();
        var shutdowns = _tiles.ShutdownAll().Concat(_pendingShutdowns).ToArray();
        // Stop whatever else still holds a shared session now, so it winds down in parallel.
        _leases.Dispose();
        try
        {
            Task.WaitAll(shutdowns, TimeSpan.FromSeconds(8));
        }
        catch (AggregateException)
        {
            // Failures cannot be shown any more; exit anyway.
        }
        base.OnClosed(e);
    }
}
```

Borrar el `ShowStats_Click` vacío si la casilla del menú alterna sola con `ToggleType="CheckBox"` + binding (quitar también `Click="ShowStats_Click"` del axaml); dejarlo solo si hace falta para que alterne en Avalonia 11.3, y en ese caso que haga `_vm.ShowStats = !_vm.ShowStats`.

- [ ] **Step 6: `Views/FullscreenWindow.axaml` y `.axaml.cs`** (misma calidad que la vista grande; Esc o doble clic cierran)

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Centinela.Desktop.Views.FullscreenWindow"
        WindowState="FullScreen" SystemDecorations="None" Background="Black" ShowInTaskbar="False"
        Icon="/Assets/centinela.ico" />
```

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Centinela.Core;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Views;

public partial class FullscreenWindow : Window
{
    readonly TileViewModel _tile;

    public FullscreenWindow() => InitializeComponent();

    public FullscreenWindow(Camera camera, bool showStats) : this()
    {
        Title = camera.Name;
        // Same stream as the camera's big view: a low-quality camera stays on its substream in fullscreen too.
        _tile = new TileViewModel(camera, ViewPlanner.StreamFor(camera, StreamKind.Main), manage: false, leases: null)
        {
            Big = true,
            ShowStats = showStats,
        };
        var view = new CameraTileView(_tile);
        view.FullscreenRequested += _ => Close();
        Content = view;
        _tile.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    /// <summary>Stops the live session; the owner tracks it until exit.</summary>
    public Task ShutdownAsync() => _tile.ShutdownAsync();
}
```

Borrar el `MainWindow.BringToFront` provisional de la Task 1 si quedó duplicado (queda el de este paso).

- [ ] **Step 7: Verificar**

Run: `dotnet test tests/Centinela.Desktop.Tests -nologo -v q` → todas pasan.
Run: `dotnet build Centinela.slnx -p:BaseOutputPath=<scratch>/bin/ -nologo -v q` → 0 advertencias, 0 errores.

- [ ] **Step 8: Commit**

```bash
git add src/Centinela.Desktop/Services/Ui/WindowPlacement.cs src/Centinela.Desktop/ViewModels/MainViewModel.cs src/Centinela.Desktop/Views/MainWindow.axaml src/Centinela.Desktop/Views/MainWindow.axaml.cs src/Centinela.Desktop/Views/FullscreenWindow.axaml src/Centinela.Desktop/Views/FullscreenWindow.axaml.cs tests/Centinela.Desktop.Tests/WindowPlacementTests.cs
git commit -m "feat(desktop): ventana principal con vistas, pantalla completa y F11"
```

---

### Task 8: Comprobación de la fase 2a

**Files:** ninguno nuevo.

- [ ] **Step 1:** `dotnet test` de Core, Platform, Media y Desktop → todas pasan. `dotnet build Centinela.slnx -p:BaseOutputPath=<scratch>/bin/` → 0 advertencias.
- [ ] **Step 2:** `git grep -n "Microsoft.Win32\|DllImport\|System.Windows" -- src/Centinela.Desktop` → nada.
- [ ] **Step 3: Prueba del usuario** (sin cerrar su Centinela WPF, que puede seguir abierto: es otra instancia solo si usa `CENTINELA_DATA_DIR`; si no, cerrar primero la WPF porque comparten la instancia única):

```bash
dotnet run --project src/Centinela.Desktop
```

Comprobar: las cámaras aparecen en las 8 vistas del desplegable; clic en una miniatura la pone en grande; en «Dos principales» sustituye a la más antigua; arrastrar reordena o pone en grande; doble clic abre pantalla completa (Esc o doble clic cierra); F11; Ver › Mostrar estadísticas; al cambiar de vista no hay pantallazo negro; al reabrir recuerda vista, tamaño y posición.
