# Centinela v1.4 — Fase 1: separar lo de Windows — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Core y Media compilan como `net10.0` (sin nada exclusivo de Windows), las piezas de sistema quedan detrás de interfaces en `Centinela.Core.Platform` con sus implementaciones de Windows en el proyecto nuevo `Centinela.Platform`, y la interfaz WPF actual las usa sin ningún cambio visible.

**Architecture:** Interfaces (`ISecretStore`, `IAutoStart`, `ISingleInstance`) y tipos puros (`OsKind`, `PlatformFolders`) en Core; implementaciones de Windows (DPAPI, registro, mutex) y el almacén de archivo multiplataforma en `Centinela.Platform`; `PlatformServices.Create()` las reúne. Media elige la aceleración por GPU y el nombre de las librerías FFmpeg según el sistema. La WPF (`Centinela.App`, `net10.0-windows`) pasa a recibir esas piezas.

**Tech Stack:** C# / .NET 10, WPF (App), FFmpeg.AutoGen 9.0.1.1, System.Security.Cryptography.ProtectedData 10.0.12 (se mueve de Core a Platform), xUnit 2.9.3, Xunit.SkippableFact 1.5.85.

**Spec:** `docs/superpowers/specs/2026-09-29-v1.4-design.md` (esta es su fase 1; las fases 2–6 tendrán su propio plan).

**Fuera de esta fase** (van en su plan): notificaciones, audio OpenAL, bandeja y sonidos propios (fase 2, con la interfaz Avalonia); implementaciones de macOS y Linux (fases 3–4); corrección de la ruta de «Arrancar con Windows» al programa instalado (fase 5, con el instalador: hacerlo ahora reescribiría la clave Run con el ejecutable de la rama de desarrollo).

**Decisión sobre el diseño:** el diseño (§5) pone el prefijo `dpapi:` a los valores de Windows. En esta fase Windows **escribe sin prefijo** (y lee con o sin él) para que la 1.3 de `master`, que el usuario sigue usando, lea el mismo `cameras.json`. El prefijo solo lo llevan los métodos nuevos (`keychain:`, `secret-service:`, `file:`), que la 1.3 no conoce ni necesita.

## Global Constraints

- .NET 10. `Centinela.Core`, `Centinela.Media`, `Centinela.Platform` y todos los proyectos de pruebas: `net10.0`. `Centinela.App`: `net10.0-windows`.
- Versión sin cambios en esta fase (`1.3.0`); la 1.4.0 se pone en la fase 6.
- Textos visibles en español; sin sistema de traducción.
- Un archivo, una responsabilidad; ningún archivo nuevo de más de ~300 líneas.
- Contraseñas nunca en texto claro en registros, avisos, UI ni en `cameras.json`.
- **Compatibilidad**: `cameras.json` escrito por esta rama debe seguir leyéndose con la 1.3 de `master` (el usuario usa las dos a la vez): en Windows las contraseñas DPAPI se escriben **sin prefijo**, como en la 1.3.
- La interfaz WPF no cambia en nada visible.
- Las pruebas no tocan datos reales: ni `%AppData%\Centinela`, ni la clave Run real, ni el registro; rutas temporales y dobles de prueba.
- Compilar la App siempre con `-p:BaseOutputPath=<scratch>\bin\` (el usuario ejecuta su Centinela desde `src/Centinela.App/bin`). No tocar ningún proceso Centinela en marcha.
- `git add` con rutas explícitas. Mensajes de commit terminados en línea en blanco + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Cero advertencias de compilación nuevas (incluido CA1416, compatibilidad de plataforma).

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/Centinela.Core/Platform/OsKind.cs` | `OsKind` (Windows, MacOS, Linux) y `OsInfo.Current`. |
| `src/Centinela.Core/Platform/ISecretStore.cs` | Contrato para guardar/leer/olvidar la contraseña de una cámara. |
| `src/Centinela.Core/Platform/SecretPrefix.cs` | Prefijos de método (`dpapi:`, `keychain:`, `secret-service:`, `file:`) y su separación. |
| `src/Centinela.Core/Platform/IAutoStart.cs` | Contrato de arranque con el sistema. |
| `src/Centinela.Core/Platform/ISingleInstance.cs` | Contrato de una sola instancia. |
| `src/Centinela.Core/Platform/PlatformFolders.cs` | Carpetas de datos/Vídeos/Imágenes/Documentos por sistema (puro). |
| `src/Centinela.Core/AppPaths.cs` *(modificar)* | Usa `PlatformFolders`. |
| `src/Centinela.Core/CameraStore.cs` *(modificar)* | Recibe `ISecretStore`; olvida secretos de cámaras borradas. |
| `src/Centinela.Core/AutoStart.cs` *(borrar)* | Pasa a Platform. |
| `src/Centinela.Platform/Windows/DpapiSecretStore.cs` | DPAPI. |
| `src/Centinela.Platform/FileSecretStore.cs` | AES-GCM con clave en archivo (multiplataforma). |
| `src/Centinela.Platform/Windows/IRunKey.cs`, `RegistryRunKey.cs`, `WindowsAutoStart.cs` | Arranque con Windows. |
| `src/Centinela.Platform/Windows/WindowsSingleInstance.cs` | Mutex + evento (código actual de `SingleInstance`). |
| `src/Centinela.Platform/PlatformServices.cs` | Reúne las implementaciones del sistema actual. |
| `src/Centinela.Media/HardwareAcceleration.cs` | Dispositivo GPU y formato de píxel por sistema. |
| `src/Centinela.Media/FFmpegLibraries.cs` | Nombre del archivo de cada librería FFmpeg por sistema. |
| `tests/Centinela.Platform.Tests/*` | Pruebas de Platform. |

---

### Task 1: Rama, destino `net10.0`, proyecto Centinela.Platform y su proyecto de pruebas

**Files:**
- Modify: `Directory.Build.props`, `src/Centinela.App/Centinela.App.csproj`
- Create: `src/Centinela.Platform/Centinela.Platform.csproj`
- Create: `tests/Centinela.Platform.Tests/Centinela.Platform.Tests.csproj`
- Create: `tests/Centinela.Platform.Tests/SmokeTests.cs`
- Modify: `Centinela.slnx`

**Interfaces:**
- Consumes: nada.
- Produces: proyecto `Centinela.Platform` (namespace raíz `Centinela.Platform`, referencia a Core, `InternalsVisibleTo Centinela.Platform.Tests`) y `Centinela.Platform.Tests` (xUnit + SkippableFact, referencia a Platform).

- [ ] **Step 1: Crear la rama**

```bash
git checkout master
git checkout -b feat/v1.4
```

- [ ] **Step 1b: Cambiar el destino común**

`Directory.Build.props`: `<TargetFramework>net10.0-windows</TargetFramework>` → `<TargetFramework>net10.0</TargetFramework>`.

`src/Centinela.App/Centinela.App.csproj`: en el primer `PropertyGroup`, añadir `<TargetFramework>net10.0-windows</TargetFramework>` (WPF sigue siendo solo de Windows).

Hasta las Tasks 2 y 3, Core usará todavía DPAPI y el registro con destino `net10.0`: se permiten **temporalmente** las advertencias CA1416 que eso produce en `CameraStore.cs` y `AutoStart.cs` (desaparecen al moverlos a Platform). Ninguna otra advertencia.

- [ ] **Step 2: Crear `src/Centinela.Platform/Centinela.Platform.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Centinela.Core\Centinela.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Centinela.Platform.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Crear `tests/Centinela.Platform.Tests/Centinela.Platform.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Xunit.SkippableFact" Version="1.5.85" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Centinela.Platform\Centinela.Platform.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Crear `tests/Centinela.Platform.Tests/SmokeTests.cs`** (se borra en la Task 2, cuando haya pruebas reales)

```csharp
namespace Centinela.Platform.Tests;

public class SmokeTests
{
    [Fact]
    public void Platform_assembly_loads() =>
        Assert.Equal("Centinela.Platform", typeof(SmokeTests).Assembly.GetReferencedAssemblies()
            .Single(a => a.Name == "Centinela.Platform").Name);
}
```

- [ ] **Step 5: Añadir los dos proyectos a `Centinela.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Centinela.App/Centinela.App.csproj" />
    <Project Path="src/Centinela.Core/Centinela.Core.csproj" />
    <Project Path="src/Centinela.Media/Centinela.Media.csproj" />
    <Project Path="src/Centinela.Platform/Centinela.Platform.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Centinela.Core.Tests/Centinela.Core.Tests.csproj" />
    <Project Path="tests/Centinela.Media.Tests/Centinela.Media.Tests.csproj" />
    <Project Path="tests/Centinela.Platform.Tests/Centinela.Platform.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 6: Compilar y ejecutar**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q`
Expected: `Superado: 1` (una prueba).
Run: `dotnet build Centinela.slnx -p:BaseOutputPath=<scratch>\bin\ -nologo -v q` → `0 Errores` (solo las CA1416 temporales del Step 1b).
Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q` → todas pasan.

- [ ] **Step 7: Commit**

```bash
git add Directory.Build.props src/Centinela.App/Centinela.App.csproj Centinela.slnx src/Centinela.Platform/Centinela.Platform.csproj tests/Centinela.Platform.Tests/Centinela.Platform.Tests.csproj tests/Centinela.Platform.Tests/SmokeTests.cs
git commit -m "chore: destino net10.0 y proyecto Centinela.Platform con sus pruebas"
```

---

### Task 2: `OsKind`, `ISecretStore` y el almacén de cámaras con secretos intercambiables

**Files:**
- Create: `src/Centinela.Core/Platform/OsKind.cs`
- Create: `src/Centinela.Core/Platform/ISecretStore.cs`
- Create: `src/Centinela.Core/Platform/SecretPrefix.cs`
- Modify: `src/Centinela.Core/CameraStore.cs`
- Modify: `src/Centinela.Core/Centinela.Core.csproj` (quitar ProtectedData)
- Create: `src/Centinela.Platform/Windows/DpapiSecretStore.cs`
- Create: `src/Centinela.Platform/FileSecretStore.cs`
- Modify: `src/Centinela.Platform/Centinela.Platform.csproj` (añadir ProtectedData)
- Create: `tests/Centinela.Core.Tests/FakeSecretStore.cs`
- Create: `tests/Centinela.Core.Tests/SecretPrefixTests.cs`
- Modify: `tests/Centinela.Core.Tests/CameraStoreTests.cs`
- Create: `tests/Centinela.Platform.Tests/DpapiSecretStoreTests.cs`
- Create: `tests/Centinela.Platform.Tests/FileSecretStoreTests.cs`
- Delete: `tests/Centinela.Platform.Tests/SmokeTests.cs`
- Modify: `src/Centinela.App/MainWindow.xaml.cs:13` y `src/Centinela.App/Centinela.App.csproj` (referencia a Platform)

**Interfaces:**
- Consumes: proyectos de la Task 1.
- Produces:
  - `enum Centinela.Core.Platform.OsKind { Windows, MacOS, Linux }`; `static OsKind OsInfo.Current`.
  - `interface ISecretStore { string Protect(Guid cameraId, string password); string? Unprotect(Guid cameraId, string storedValue); void Forget(Guid cameraId, string storedValue); }`
  - `static class SecretPrefix { const string Dpapi = "dpapi:", Keychain = "keychain:", SecretService = "secret-service:", File = "file:"; static (string Prefix, string Payload) Split(string stored); }` — `Prefix` es `""` si no lleva ninguno conocido.
  - `CameraStore(string filePath, ISecretStore secrets)` (el constructor de un argumento desaparece).
  - `DpapiSecretStore : ISecretStore` (Windows; escribe base64 sin prefijo).
  - `FileSecretStore(string keyFile) : ISecretStore` (`file:` + AES-GCM).

- [ ] **Step 1: Escribir las pruebas de Core**

`tests/Centinela.Core.Tests/FakeSecretStore.cs`:

```csharp
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

/// <summary>Reversible stand-in: "fake:" + base64. Anything else cannot be read. Records forgotten ids.</summary>
sealed class FakeSecretStore : ISecretStore
{
    public readonly List<Guid> Forgotten = [];

    public string Protect(Guid cameraId, string password) =>
        password.Length == 0 ? "" : "fake:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        if (!storedValue.StartsWith("fake:", StringComparison.Ordinal)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(storedValue["fake:".Length..])); }
        catch (FormatException) { return null; }
    }

    public void Forget(Guid cameraId, string storedValue) => Forgotten.Add(cameraId);
}
```

`tests/Centinela.Core.Tests/SecretPrefixTests.cs`:

```csharp
using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

public class SecretPrefixTests
{
    [Theory]
    [InlineData("dpapi:QUJD", "dpapi:", "QUJD")]
    [InlineData("keychain:8b1f", "keychain:", "8b1f")]
    [InlineData("secret-service:8b1f", "secret-service:", "8b1f")]
    [InlineData("file:AAAA", "file:", "AAAA")]
    [InlineData("QUJD", "", "QUJD")]          // 1.3 files: plain DPAPI base64
    [InlineData("other:x", "", "other:x")]
    [InlineData("", "", "")]
    public void Split_separates_known_prefixes(string stored, string prefix, string payload) =>
        Assert.Equal((prefix, payload), SecretPrefix.Split(stored));
}
```

En `tests/Centinela.Core.Tests/CameraStoreTests.cs`, añadir el campo `readonly FakeSecretStore _secrets = new();` bajo `FilePath`, sustituir todas las apariciones de `new CameraStore(FilePath)` por `new CameraStore(FilePath, _secrets)` y de `new CameraStore(path)` por `new CameraStore(path, _secrets)`, añadir `using Centinela.Core.Platform;` y estas dos pruebas al final de la clase:

```csharp
    [Fact]
    public void Passwords_go_through_the_secret_store()
    {
        new CameraStore(FilePath, _secrets).Save([Sample(0)]);
        Assert.Contains("\"passwordProtected\": \"fake:", File.ReadAllText(FilePath));
        Assert.Equal("SuperSecreta123", Assert.Single(new CameraStore(FilePath, _secrets).Load()).Password);
    }

    [Fact]
    public void Saving_without_a_camera_forgets_its_secret_and_keeps_the_others()
    {
        var kept = Sample(0);
        var removed = Sample(1);
        var store = new CameraStore(FilePath, _secrets);
        store.Save([kept, removed]);
        store.Save([kept]);
        Assert.Equal([removed.Id], _secrets.Forgotten);
    }
```

- [ ] **Step 2: Ejecutar para ver que fallan**

Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q`
Expected: error de compilación (`Centinela.Core.Platform` y el constructor de dos argumentos no existen).

- [ ] **Step 3: Implementar en Core**

`src/Centinela.Core/Platform/OsKind.cs`:

```csharp
namespace Centinela.Core.Platform;

public enum OsKind { Windows, MacOS, Linux }

public static class OsInfo
{
    /// <summary>The system this process runs on; anything that is neither Windows nor macOS counts as Linux.</summary>
    public static OsKind Current =>
        OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.MacOS : OsKind.Linux;
}
```

`src/Centinela.Core/Platform/ISecretStore.cs`:

```csharp
namespace Centinela.Core.Platform;

/// <summary>
/// Where a camera's password lives: the value returned by <see cref="Protect"/> is what cameras.json keeps (the
/// encrypted password itself, or only a reference when the password sits in the system keychain).
/// </summary>
public interface ISecretStore
{
    /// <summary>Stores the password; returns the value to keep in cameras.json ("" for an empty password).</summary>
    string Protect(Guid cameraId, string password);

    /// <summary>The password behind a stored value; "" for an empty value; null when this system cannot read it.</summary>
    string? Unprotect(Guid cameraId, string storedValue);

    /// <summary>Forgets a deleted camera's password. No-op when nothing is held outside the stored value.</summary>
    void Forget(Guid cameraId, string storedValue);
}
```

`src/Centinela.Core/Platform/SecretPrefix.cs`:

```csharp
namespace Centinela.Core.Platform;

/// <summary>
/// The method a stored password was saved with. A value without a known prefix is a Centinela 1.3 value
/// (DPAPI, Windows).
/// </summary>
public static class SecretPrefix
{
    public const string Dpapi = "dpapi:";
    public const string Keychain = "keychain:";
    public const string SecretService = "secret-service:";
    public const string File = "file:";

    static readonly string[] Known = [Dpapi, Keychain, SecretService, File];

    public static (string Prefix, string Payload) Split(string stored)
    {
        foreach (var prefix in Known)
            if (stored.StartsWith(prefix, StringComparison.Ordinal)) return (prefix, stored[prefix.Length..]);
        return ("", stored);
    }
}
```

En `src/Centinela.Core/CameraStore.cs`:
- Cambiar la cabecera a `public sealed class CameraStore(string filePath, ISecretStore secrets)` y añadir `using Centinela.Core.Platform;`; quitar `using System.Security.Cryptography;` y `using System.Text;` si ya no se usan.
- `ToDto` y `FromDto` pasan de `static` a métodos de instancia.
- En `ToDto`: `PasswordProtected = secrets.Protect(c.Id, password),`.
- En `FromDto`: `Password = secrets.Unprotect(d.Id, d.PasswordProtected ?? "") ?? "",`.
- Borrar los métodos `Protect` y `Unprotect` estáticos.
- Sustituir `Save` por:

```csharp
    public void Save(IEnumerable<Camera> cameras)
    {
        var list = cameras.ToList();
        var previous = ReadStoredSecrets();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list.Select(ToDto).ToList(), Json));
        File.Move(tmp, filePath, overwrite: true);

        // A camera that is no longer in the list takes its password with it (keychain entries, etc.).
        var kept = list.Select(c => c.Id).ToHashSet();
        foreach (var (id, stored) in previous)
            if (!kept.Contains(id) && stored.Length > 0) secrets.Forget(id, stored);
    }

    /// <summary>Id → stored password value of the file as it is now; empty when missing or unreadable.</summary>
    Dictionary<Guid, string> ReadStoredSecrets()
    {
        try
        {
            if (!File.Exists(filePath)) return [];
            var dtos = JsonSerializer.Deserialize<List<CameraDto>>(File.ReadAllText(filePath), Json) ?? [];
            return dtos.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().PasswordProtected ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
```

En `src/Centinela.Core/Centinela.Core.csproj`, borrar el `ItemGroup` con `System.Security.Cryptography.ProtectedData`.

- [ ] **Step 4: Ejecutar las pruebas de Core**

Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q`
Expected: todas pasan (las dos pruebas de contraseña ilegible siguen pasando: el doble devuelve null para `AAAA` y `not base64!`).

- [ ] **Step 5: Pruebas de Platform**

Borrar `tests/Centinela.Platform.Tests/SmokeTests.cs`.

`tests/Centinela.Platform.Tests/DpapiSecretStoreTests.cs`:

```csharp
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class DpapiSecretStoreTests
{
    static readonly Guid Id = Guid.NewGuid();

    [SkippableFact]
    public void Round_trips_without_a_prefix_so_centinela_1_3_can_still_read_it()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI solo existe en Windows");
        var store = new DpapiSecretStore();
        var stored = store.Protect(Id, "SuperSecreta123");
        Assert.DoesNotContain(":", stored);
        Assert.DoesNotContain("SuperSecreta123", stored);
        Assert.Equal("SuperSecreta123", store.Unprotect(Id, stored));
        Assert.Equal("SuperSecreta123", store.Unprotect(Id, "dpapi:" + stored));
    }

    [SkippableFact]
    public void Empty_unreadable_and_foreign_values()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI solo existe en Windows");
        var store = new DpapiSecretStore();
        Assert.Equal("", store.Protect(Id, ""));
        Assert.Equal("", store.Unprotect(Id, ""));
        Assert.Null(store.Unprotect(Id, "AAAA"));
        Assert.Null(store.Unprotect(Id, "not base64!"));
        Assert.Null(store.Unprotect(Id, "keychain:" + Id));
    }
}
```

`tests/Centinela.Platform.Tests/FileSecretStoreTests.cs`:

```csharp
namespace Centinela.Platform.Tests;

public sealed class FileSecretStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-secrets-" + Guid.NewGuid());
    string KeyFile => Path.Combine(_dir, "secret.key");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Round_trips_with_the_file_prefix_and_creates_the_key_once()
    {
        var id = Guid.NewGuid();
        var stored = new FileSecretStore(KeyFile).Protect(id, "SuperSecreta123");
        Assert.StartsWith("file:", stored);
        Assert.DoesNotContain("SuperSecreta123", stored);
        var key = File.ReadAllBytes(KeyFile);
        Assert.Equal(32, key.Length);
        // A new instance reads the same key.
        Assert.Equal("SuperSecreta123", new FileSecretStore(KeyFile).Unprotect(id, stored));
        Assert.Equal(key, File.ReadAllBytes(KeyFile));
    }

    [Fact]
    public void A_value_is_bound_to_its_camera()
    {
        var store = new FileSecretStore(KeyFile);
        var stored = store.Protect(Guid.NewGuid(), "x");
        Assert.Null(store.Unprotect(Guid.NewGuid(), stored));
    }

    [Fact]
    public void Empty_foreign_and_damaged_values()
    {
        var store = new FileSecretStore(KeyFile);
        var id = Guid.NewGuid();
        Assert.Equal("", store.Protect(id, ""));
        Assert.Equal("", store.Unprotect(id, ""));
        Assert.Null(store.Unprotect(id, "QUJD"));             // DPAPI value from Windows
        Assert.Null(store.Unprotect(id, "file:AAAA"));        // too short
        Assert.Null(store.Unprotect(id, "file:not base64!"));
    }

    [SkippableFact]
    public void Key_file_is_readable_only_by_its_owner_on_unix()
    {
        Skip.If(OperatingSystem.IsWindows(), "permisos Unix");
        new FileSecretStore(KeyFile).Protect(Guid.NewGuid(), "x");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile));
    }
}
```

- [ ] **Step 6: Implementar en Platform**

En `src/Centinela.Platform/Centinela.Platform.csproj`, añadir:

```xml
  <ItemGroup>
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.12" />
  </ItemGroup>
```

`src/Centinela.Platform/Windows/DpapiSecretStore.cs`:

```csharp
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

/// <summary>
/// DPAPI (current Windows user). Writes the plain base64 of Centinela 1.3, without a prefix, so both versions
/// read the same cameras.json; reads it with or without "dpapi:".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    public string Protect(Guid cameraId, string password) => password.Length == 0
        ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        var (prefix, payload) = SecretPrefix.Split(storedValue);
        if (prefix is not ("" or SecretPrefix.Dpapi)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(payload), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Another Windows user's file, or damaged: the camera loses its password.
            return null;
        }
    }

    public void Forget(Guid cameraId, string storedValue) { }
}
```

`src/Centinela.Platform/FileSecretStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform;

/// <summary>
/// AES-256-GCM with a random key kept in <paramref name="keyFile"/> (owner-only on Unix). For systems without a
/// keychain and for test runs. Each value is bound to its camera (the camera Id is the associated data).
/// Stored form: "file:" + base64(nonce[12] | tag[16] | ciphertext).
/// </summary>
public sealed class FileSecretStore(string keyFile) : ISecretStore
{
    const int NonceSize = 12;
    const int TagSize = 16;
    byte[]? _key;

    public string Protect(Guid cameraId, string password)
    {
        if (password.Length == 0) return "";
        var plain = Encoding.UTF8.GetBytes(password);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Key(), TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, cameraId.ToByteArray());
        return SecretPrefix.File + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(Guid cameraId, string storedValue)
    {
        if (storedValue.Length == 0) return "";
        var (prefix, payload) = SecretPrefix.Split(storedValue);
        if (prefix != SecretPrefix.File) return null;
        try
        {
            var data = Convert.FromBase64String(payload);
            if (data.Length < NonceSize + TagSize) return null;
            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(Key(), TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize),
                plain, cameraId.ToByteArray());
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null;
        }
    }

    public void Forget(Guid cameraId, string storedValue) { }

    byte[] Key()
    {
        if (_key is not null) return _key;
        if (File.Exists(keyFile))
        {
            var existing = File.ReadAllBytes(keyFile);
            if (existing.Length == 32) return _key = existing;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, key);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return _key = key;
    }
}
```

- [ ] **Step 7: Ejecutar las pruebas de Platform**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q`
Expected: todas pasan en Windows (la de permisos Unix, omitida).

- [ ] **Step 8: La App usa DPAPI a través de Platform**

En `src/Centinela.App/Centinela.App.csproj`, añadir `<ProjectReference Include="..\Centinela.Platform\Centinela.Platform.csproj" />` al `ItemGroup` de referencias.

En `src/Centinela.App/MainWindow.xaml.cs:13`, sustituir
`readonly CameraStore _store = new(CameraStore.DefaultPath);`
por
`readonly CameraStore _store = new(CameraStore.DefaultPath, new Centinela.Platform.Windows.DpapiSecretStore());`
(la Task 6 lo cambia por `PlatformServices`).

- [ ] **Step 9: Compilar la App y ejecutar todas las pruebas de Core y Platform**

Run: `dotnet build src/Centinela.App -p:BaseOutputPath=<scratch>\bin\ -nologo -v q` → `0 Errores`.
Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q` y `dotnet test tests/Centinela.Platform.Tests -nologo -v q` → todas pasan.

- [ ] **Step 10: Commit**

```bash
git add src/Centinela.Core/Platform/OsKind.cs src/Centinela.Core/Platform/ISecretStore.cs src/Centinela.Core/Platform/SecretPrefix.cs src/Centinela.Core/CameraStore.cs src/Centinela.Core/Centinela.Core.csproj src/Centinela.Platform/Centinela.Platform.csproj src/Centinela.Platform/Windows/DpapiSecretStore.cs src/Centinela.Platform/FileSecretStore.cs tests/Centinela.Core.Tests/FakeSecretStore.cs tests/Centinela.Core.Tests/SecretPrefixTests.cs tests/Centinela.Core.Tests/CameraStoreTests.cs tests/Centinela.Platform.Tests/DpapiSecretStoreTests.cs tests/Centinela.Platform.Tests/FileSecretStoreTests.cs src/Centinela.App/Centinela.App.csproj src/Centinela.App/MainWindow.xaml.cs
git rm tests/Centinela.Platform.Tests/SmokeTests.cs
git commit -m "refactor: contraseñas de cámaras detrás de ISecretStore (DPAPI y archivo)"
```

---

### Task 3: Arranque con el sistema detrás de `IAutoStart`

**Files:**
- Create: `src/Centinela.Core/Platform/IAutoStart.cs`
- Delete: `src/Centinela.Core/AutoStart.cs`
- Create: `src/Centinela.Platform/Windows/IRunKey.cs`
- Create: `src/Centinela.Platform/Windows/RegistryRunKey.cs`
- Create: `src/Centinela.Platform/Windows/WindowsAutoStart.cs`
- Move: `tests/Centinela.Core.Tests/AutoStartTests.cs` → `tests/Centinela.Platform.Tests/WindowsAutoStartTests.cs`
- Modify: `src/Centinela.App/MainWindow.Tray.cs:10,17,20`

**Interfaces:**
- Consumes: Task 2 (namespace `Centinela.Core.Platform`).
- Produces: `interface IAutoStart { bool IsEnabled { get; } void Enable(); void Disable(); bool Migrate(); }`; `WindowsAutoStart(IRunKey key, string exePath) : IAutoStart` con `string Command`; `interface IRunKey` y `RegistryRunKey` en `Centinela.Platform.Windows`.

- [ ] **Step 1: Mover las pruebas**

```bash
git mv tests/Centinela.Core.Tests/AutoStartTests.cs tests/Centinela.Platform.Tests/WindowsAutoStartTests.cs
```

En el archivo movido: `using Centinela.Core;` → `using Centinela.Platform.Windows;`; `namespace Centinela.Core.Tests;` → `namespace Centinela.Platform.Tests;`; `public class AutoStartTests` → `public class WindowsAutoStartTests`; todas las `new AutoStart(` → `new WindowsAutoStart(`; todas las `.MigrateLegacy()` → `.Migrate()`.

- [ ] **Step 2: Ejecutar para ver que fallan**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q`
Expected: error de compilación (`WindowsAutoStart` no existe).

- [ ] **Step 3: Implementar**

`src/Centinela.Core/Platform/IAutoStart.cs`:

```csharp
namespace Centinela.Core.Platform;

/// <summary>Starts Centinela hidden in the tray (<c>--tray</c>) when the user logs in.</summary>
public interface IAutoStart
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();

    /// <summary>Repairs an entry left by an older version (e.g. the CamaraWin name). True when it changed something.</summary>
    bool Migrate();
}
```

`src/Centinela.Platform/Windows/IRunKey.cs`:

```csharp
namespace Centinela.Platform.Windows;

/// <summary>The HKCU Run key, behind an interface so tests never touch the real registry.</summary>
public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}
```

`src/Centinela.Platform/Windows/RegistryRunKey.cs`:

```csharp
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Centinela.Platform.Windows;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run — no admin rights needed.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryRunKey : IRunKey
{
    const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, value);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
```

`src/Centinela.Platform/Windows/WindowsAutoStart.cs`:

```csharp
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

/// <summary>A value in the HKCU Run key that starts this exe with <c>--tray</c>.</summary>
public sealed class WindowsAutoStart(IRunKey key, string exePath) : IAutoStart
{
    const string ValueName = "Centinela";
    /// <summary>The entry written before the app was renamed from CamaraWin; it points at the old exe.</summary>
    const string LegacyValueName = "CamaraWin";

    public string Command => $"\"{exePath}\" --tray";
    public bool IsEnabled => string.Equals(key.Get(ValueName), Command, StringComparison.OrdinalIgnoreCase);

    public void Enable()
    {
        key.Delete(LegacyValueName);
        key.Set(ValueName, Command);
    }

    public void Disable()
    {
        key.Delete(LegacyValueName);
        key.Delete(ValueName);
    }

    /// <summary>If the pre-rename entry exists, replaces it with one for this exe.</summary>
    public bool Migrate()
    {
        if (key.Get(LegacyValueName) is null) return false;
        Enable();
        return true;
    }
}
```

Borrar `src/Centinela.Core/AutoStart.cs` (`git rm`).

- [ ] **Step 4: La App**

En `src/Centinela.App/MainWindow.Tray.cs`: añadir `using Centinela.Core.Platform;`; línea 10 `AutoStart _autoStart = null!;` → `IAutoStart _autoStart = null!;`; línea 17 → `_autoStart = new Centinela.Platform.Windows.WindowsAutoStart(new Centinela.Platform.Windows.RegistryRunKey(), Environment.ProcessPath!);` (la Task 6 lo cambia por `PlatformServices`); línea 20 `_autoStart.MigrateLegacy()` → `_autoStart.Migrate()`.

- [ ] **Step 5: Verificar**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q` → pasan (incluidas las 4 de arranque).
Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q` → pasan.
Run: `dotnet build src/Centinela.App -p:BaseOutputPath=<scratch>\bin\ -nologo -v q` → `0 Errores`.

- [ ] **Step 6: Commit**

```bash
git add src/Centinela.Core/Platform/IAutoStart.cs src/Centinela.Platform/Windows/IRunKey.cs src/Centinela.Platform/Windows/RegistryRunKey.cs src/Centinela.Platform/Windows/WindowsAutoStart.cs tests/Centinela.Platform.Tests/WindowsAutoStartTests.cs src/Centinela.App/MainWindow.Tray.cs
git rm src/Centinela.Core/AutoStart.cs
git commit -m "refactor: arranque con el sistema detrás de IAutoStart"
```

---

### Task 4: Una sola instancia detrás de `ISingleInstance`

**Files:**
- Create: `src/Centinela.Core/Platform/ISingleInstance.cs`
- Create: `src/Centinela.Platform/Windows/WindowsSingleInstance.cs`
- Delete: `src/Centinela.App/SingleInstance.cs`
- Modify: `src/Centinela.App/App.xaml.cs:12,45`
- Create: `tests/Centinela.Platform.Tests/WindowsSingleInstanceTests.cs`

**Interfaces:**
- Consumes: Task 2 (namespace).
- Produces: `interface ISingleInstance { bool TryClaim(); void ListenForActivation(Action onActivate); }`; `WindowsSingleInstance(string instanceSuffix) : ISingleInstance, IDisposable`.

- [ ] **Step 1: Escribir la prueba** (`tests/Centinela.Platform.Tests/WindowsSingleInstanceTests.cs`; usa un sufijo aleatorio para no tocar nunca la instancia del usuario)

```csharp
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class WindowsSingleInstanceTests
{
    [SkippableFact]
    public void Second_claim_fails_and_wakes_the_first()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "mutex con nombre de Windows");
        var suffix = ".test-" + Guid.NewGuid().ToString("N");
        using var first = new WindowsSingleInstance(suffix);
        Assert.True(first.TryClaim());
        using var woken = new ManualResetEventSlim();
        first.ListenForActivation(woken.Set);

        // A second claimant must run on another thread: a Windows mutex is re-entrant for its owner thread.
        var second = Task.Run(() =>
        {
            using var other = new WindowsSingleInstance(suffix);
            return other.TryClaim();
        });
        Assert.False(second.Result);
        Assert.True(woken.Wait(TimeSpan.FromSeconds(5)));
    }
}
```

- [ ] **Step 2: Ejecutar para ver que falla**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q`
Expected: error de compilación (`WindowsSingleInstance` no existe).

- [ ] **Step 3: Implementar**

`src/Centinela.Core/Platform/ISingleInstance.cs`:

```csharp
namespace Centinela.Core.Platform;

/// <summary>One Centinela per user session: a later launch wakes the first one and exits.</summary>
public interface ISingleInstance
{
    /// <summary>True for the first instance; a later instance signals the first one and gets false.</summary>
    bool TryClaim();

    /// <summary>Calls <paramref name="onActivate"/> on a background thread each time a later instance starts.</summary>
    void ListenForActivation(Action onActivate);
}
```

`src/Centinela.Platform/Windows/WindowsSingleInstance.cs` (el código de `src/Centinela.App/SingleInstance.cs` pasado a instancia; `instanceSuffix` es `AppPaths.InstanceSuffix` en la App):

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Centinela.Core.Platform;

namespace Centinela.Platform.Windows;

/// <summary>
/// A named mutex marks the first instance; a named event wakes it. A CENTINELA_DATA_DIR run passes its own
/// suffix, so a test build never wakes (or yields to) the user's instance.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSingleInstance(string instanceSuffix) : ISingleInstance, IDisposable
{
    const int AnyProcess = -1;   // ASFW_ANY
    readonly string _mutexName = @"Local\Centinela.SingleInstance" + instanceSuffix;
    readonly string _eventName = @"Local\Centinela.Activate" + instanceSuffix;
    // Held for the life of the process; the OS releases the mutex when the process ends.
    Mutex? _mutex;
    EventWaitHandle? _activate;

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <remarks>
    /// A launch while the first instance is exiting (mutex still held during its exit wait) is absorbed: the signal
    /// is ignored and nothing opens, so the user simply launches again.
    /// </remarks>
    public bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (createdNew)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
            return true;
        }
        try
        {
            using var existing = EventWaitHandle.OpenExisting(_eventName);
            // This launch owns the foreground; let the first instance bring its window forward.
            AllowSetForegroundWindow(AnyProcess);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // First instance is still starting; nothing to activate.
        }
        return false;
    }

    public void ListenForActivation(Action onActivate)
    {
        var handle = _activate ?? throw new InvalidOperationException("Not the first instance.");
        new Thread(() =>
        {
            try
            {
                while (handle.WaitOne()) onActivate();
            }
            catch (ObjectDisposedException)
            {
                // Disposed (tests): stop listening.
            }
        }) { IsBackground = true, Name = "SingleInstance" }.Start();
    }

    public void Dispose()
    {
        _activate?.Dispose();
        _mutex?.Dispose();
    }
}
```

`git rm src/Centinela.App/SingleInstance.cs`.

En `src/Centinela.App/App.xaml.cs`: añadir el campo `static Centinela.Core.Platform.ISingleInstance _instance = null!;` en la clase; en `OnStartup`, antes del `if`, `_instance = new Centinela.Platform.Windows.WindowsSingleInstance(AppPaths.InstanceSuffix);` (la Task 6 lo cambia por `PlatformServices`); línea 12 `SingleInstance.TryClaim()` → `_instance.TryClaim()`; línea 45 `SingleInstance.ListenForActivation(` → `_instance.ListenForActivation(`. El objeto vive lo que dure el proceso (no se desecha).

- [ ] **Step 4: Verificar**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q` → pasan.
Run: `dotnet build src/Centinela.App -p:BaseOutputPath=<scratch>\bin\ -nologo -v q` → `0 Errores`.

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Core/Platform/ISingleInstance.cs src/Centinela.Platform/Windows/WindowsSingleInstance.cs tests/Centinela.Platform.Tests/WindowsSingleInstanceTests.cs src/Centinela.App/App.xaml.cs
git rm src/Centinela.App/SingleInstance.cs
git commit -m "refactor: una sola instancia detrás de ISingleInstance"
```

---

### Task 5: Carpetas del usuario por sistema (`PlatformFolders`) y `AppPaths`

**Files:**
- Create: `src/Centinela.Core/Platform/PlatformFolders.cs`
- Modify: `src/Centinela.Core/AppPaths.cs`
- Create: `tests/Centinela.Core.Tests/PlatformFoldersTests.cs`
- Modify: `tests/Centinela.Core.Tests/AppPathsTests.cs`

**Interfaces:**
- Consumes: `OsKind` (Task 2).
- Produces: `sealed record UserFolders(string Data, string Videos, string Pictures, string Documents)`; `static UserFolders PlatformFolders.For(OsKind os, string home, Func<string, string?> env, Func<Environment.SpecialFolder, string> special, string? userDirs)`; `static UserFolders PlatformFolders.Current` (valores reales, calculado una vez); `AppPaths.Folders` (= `PlatformFolders.Current`).

- [ ] **Step 1: Escribir las pruebas** (`tests/Centinela.Core.Tests/PlatformFoldersTests.cs`)

```csharp
using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

public class PlatformFoldersTests
{
    static string Special(Environment.SpecialFolder f) => f switch
    {
        Environment.SpecialFolder.ApplicationData => @"C:\Users\ana\AppData\Roaming",
        Environment.SpecialFolder.MyVideos => @"C:\Users\ana\Videos",
        Environment.SpecialFolder.MyPictures => @"C:\Users\ana\Pictures",
        Environment.SpecialFolder.MyDocuments => @"C:\Users\ana\Documents",
        _ => "",
    };

    static string? NoEnv(string name) => null;

    [Fact]
    public void Windows_uses_the_known_folders()
    {
        var f = PlatformFolders.For(OsKind.Windows, @"C:\Users\ana", NoEnv, Special, null);
        Assert.Equal(new UserFolders(Path.Combine(@"C:\Users\ana\AppData\Roaming", "Centinela"),
            @"C:\Users\ana\Videos", @"C:\Users\ana\Pictures", @"C:\Users\ana\Documents"), f);
    }

    [Fact]
    public void MacOS_uses_application_support_and_movies()
    {
        var f = PlatformFolders.For(OsKind.MacOS, "/Users/ana", NoEnv, Special, null);
        Assert.Equal(new UserFolders("/Users/ana/Library/Application Support/Centinela",
            "/Users/ana/Movies", "/Users/ana/Pictures", "/Users/ana/Documents"), f);
    }

    [Fact]
    public void Linux_defaults_to_xdg_locations()
    {
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", NoEnv, Special, null);
        Assert.Equal(new UserFolders("/home/ana/.local/share/centinela",
            "/home/ana/Videos", "/home/ana/Pictures", "/home/ana/Documents"), f);
    }

    [Fact]
    public void Linux_honors_xdg_data_home_and_localized_user_dirs()
    {
        const string userDirs = """
            # This file is written by xdg-user-dirs-update
            XDG_DOCUMENTS_DIR="$HOME/Documentos"
            XDG_PICTURES_DIR="$HOME/Imágenes"
            XDG_VIDEOS_DIR="/media/datos/Vídeos"
            """;
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana",
            name => name == "XDG_DATA_HOME" ? "/home/ana/.datos" : null, Special, userDirs);
        Assert.Equal(new UserFolders("/home/ana/.datos/centinela",
            "/media/datos/Vídeos", "/home/ana/Imágenes", "/home/ana/Documentos"), f);
    }

    [Fact]
    public void Linux_ignores_a_user_dir_that_points_at_home_itself()
    {
        // xdg-user-dirs sets a folder to $HOME when the user disabled it.
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", NoEnv, Special, "XDG_VIDEOS_DIR=\"$HOME/\"");
        Assert.Equal("/home/ana/Videos", f.Videos);
    }
}
```

En `tests/Centinela.Core.Tests/AppPathsTests.cs`, en `Override_redirects_every_user_folder_and_isolates_the_instance`, sustituir las tres aserciones sin override por:

```csharp
            Assert.Equal(Path.Combine(AppPaths.Folders.Videos, "Centinela"), AppPaths.RecordingsDirectory);
            Assert.Equal(Path.Combine(AppPaths.Folders.Pictures, "Centinela"), AppPaths.SnapshotsDirectory);
            Assert.Equal(Path.Combine(AppPaths.Folders.Documents, "Centinela"), AppPaths.DefaultBackupDirectory);
```

y en `DataDirectory_honors_override_variable_when_set` sustituir el cálculo de `defaultPath` por `var defaultPath = AppPaths.Folders.Data;`.

- [ ] **Step 2: Ejecutar para ver que fallan**

Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q`
Expected: error de compilación (`PlatformFolders`, `UserFolders`, `AppPaths.Folders` no existen).

- [ ] **Step 3: Implementar**

`src/Centinela.Core/Platform/PlatformFolders.cs`:

```csharp
namespace Centinela.Core.Platform;

/// <param name="Data">Centinela's own data folder (cameras, settings, logs).</param>
/// <param name="Videos">The user's videos folder (recordings go in a «Centinela» folder inside).</param>
public sealed record UserFolders(string Data, string Videos, string Pictures, string Documents);

/// <summary>Where each system keeps app data and the user's videos, pictures and documents.</summary>
public static class PlatformFolders
{
    static readonly Lazy<UserFolders> Real = new(() => For(OsInfo.Current,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable,
        Environment.GetFolderPath, ReadUserDirs()));

    /// <summary>This process's folders (computed once).</summary>
    public static UserFolders Current => Real.Value;

    /// <param name="userDirs">Contents of Linux's <c>user-dirs.dirs</c>, or null.</param>
    public static UserFolders For(OsKind os, string home, Func<string, string?> env,
        Func<Environment.SpecialFolder, string> special, string? userDirs) => os switch
    {
        OsKind.Windows => new UserFolders(Path.Combine(special(Environment.SpecialFolder.ApplicationData), "Centinela"),
            special(Environment.SpecialFolder.MyVideos), special(Environment.SpecialFolder.MyPictures),
            special(Environment.SpecialFolder.MyDocuments)),
        OsKind.MacOS => new UserFolders($"{home}/Library/Application Support/Centinela",
            $"{home}/Movies", $"{home}/Pictures", $"{home}/Documents"),
        _ => Linux(home, env, userDirs),
    };

    static UserFolders Linux(string home, Func<string, string?> env, string? userDirs)
    {
        var dataHome = env("XDG_DATA_HOME") is { Length: > 0 } d ? d : $"{home}/.local/share";
        var dirs = ParseUserDirs(userDirs, home);
        string Dir(string key, string fallback) => dirs.TryGetValue(key, out var value) ? value : $"{home}/{fallback}";
        return new UserFolders($"{dataHome}/centinela",
            Dir("XDG_VIDEOS_DIR", "Videos"), Dir("XDG_PICTURES_DIR", "Pictures"), Dir("XDG_DOCUMENTS_DIR", "Documents"));
    }

    /// <summary>Lines like <c>XDG_VIDEOS_DIR="$HOME/Vídeos"</c>; entries equal to $HOME itself are skipped.</summary>
    static Dictionary<string, string> ParseUserDirs(string? text, string home)
    {
        var result = new Dictionary<string, string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var value = line[(equals + 1)..].Trim().Trim('"').Replace("$HOME", home, StringComparison.Ordinal).TrimEnd('/');
            if (value.Length == 0 || value == home.TrimEnd('/')) continue;
            result[line[..equals].Trim()] = value;
        }
        return result;
    }

    static string? ReadUserDirs()
    {
        if (!OperatingSystem.IsLinux()) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : $"{home}/.config";
        try
        {
            var file = Path.Combine(config, "user-dirs.dirs");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

En `src/Centinela.Core/AppPaths.cs`: añadir `using Centinela.Core.Platform;` y

```csharp
    /// <summary>This system's folders (Windows: AppData, Vídeos…; macOS: Application Support, Películas…; Linux: XDG).</summary>
    public static UserFolders Folders => PlatformFolders.Current;
```

y sustituir:
- `DataDirectory` → `Override ?? Folders.Data;`
- `RecordingsDirectory` → `UserFolder(Folders.Videos, "recordings");`
- `SnapshotsDirectory` → `UserFolder(Folders.Pictures, "snapshots");`
- `DefaultBackupDirectory` → `UserFolder(Folders.Documents, "backup");`
- `UserFolder` → `static string UserFolder(string folder, string overrideName) => Override is { } root ? Path.Combine(root, overrideName) : Path.Combine(folder, AppFolder);`

- [ ] **Step 4: Ejecutar las pruebas**

Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q`
Expected: todas pasan (en Windows las rutas son exactamente las de la 1.3).

- [ ] **Step 5: Commit**

```bash
git add src/Centinela.Core/Platform/PlatformFolders.cs src/Centinela.Core/AppPaths.cs tests/Centinela.Core.Tests/PlatformFoldersTests.cs tests/Centinela.Core.Tests/AppPathsTests.cs
git commit -m "refactor: carpetas de datos, vídeos e imágenes por sistema"
```

---

### Task 6: `PlatformServices` y la App con todas las piezas desde un solo sitio

**Files:**
- Create: `src/Centinela.Platform/PlatformServices.cs`
- Create: `tests/Centinela.Platform.Tests/PlatformServicesTests.cs`
- Modify: `src/Centinela.App/App.xaml.cs`, `src/Centinela.App/MainWindow.xaml.cs:13`, `src/Centinela.App/MainWindow.Tray.cs:17`

**Interfaces:**
- Consumes: Tasks 2–5.
- Produces: `sealed class PlatformServices { ISecretStore Secrets; IAutoStart? AutoStart; ISingleInstance SingleInstance; static PlatformServices Create(string exePath); }` y en la App `App.Platform` (estático) que usan `MainWindow` y el arranque.

- [ ] **Step 1: Escribir la prueba** (`tests/Centinela.Platform.Tests/PlatformServicesTests.cs`)

```csharp
using Centinela.Core;
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class PlatformServicesTests
{
    [SkippableFact]
    public void Windows_gets_dpapi_the_run_key_and_the_mutex()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "servicios de Windows");
        var original = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
            var services = PlatformServices.Create(@"C:\Apps\Centinela\Centinela.exe");
            Assert.IsType<DpapiSecretStore>(services.Secrets);
            Assert.IsType<WindowsAutoStart>(services.AutoStart);
            Assert.IsType<WindowsSingleInstance>(services.SingleInstance);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, original);
        }
    }

    [Fact]
    public void A_test_data_folder_gets_the_file_store_and_no_auto_start()
    {
        var original = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        var dir = Path.Combine(Path.GetTempPath(), "centinela-svc-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, dir);
            var services = PlatformServices.Create("/opt/centinela/Centinela");
            Assert.IsType<FileSecretStore>(services.Secrets);
            Assert.Null(services.AutoStart);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, original);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Ejecutar para ver que falla**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q`
Expected: error de compilación (`PlatformServices` no existe).

- [ ] **Step 3: Implementar** (`src/Centinela.Platform/PlatformServices.cs`)

```csharp
using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Platform.Windows;

namespace Centinela.Platform;

/// <summary>
/// The system pieces for this process. With CENTINELA_DATA_DIR (tests, smoke runs) nothing real is touched:
/// passwords go to a key file in that folder and there is no auto start. macOS and Linux arrive in phases 3–4.
/// </summary>
public sealed class PlatformServices
{
    public required ISecretStore Secrets { get; init; }
    /// <summary>Null when starting with the system is not offered (test data folder, or not supported here).</summary>
    public IAutoStart? AutoStart { get; init; }
    public required ISingleInstance SingleInstance { get; init; }

    public static PlatformServices Create(string exePath)
    {
        var testRun = AppPaths.IsDataDirectoryOverridden;
        ISecretStore? testSecrets = testRun ? new FileSecretStore(Path.Combine(AppPaths.DataDirectory, "secret.key")) : null;
        if (OperatingSystem.IsWindows())
        {
            return new PlatformServices
            {
                Secrets = testSecrets ?? new DpapiSecretStore(),
                AutoStart = testRun ? null : new WindowsAutoStart(new RegistryRunKey(), exePath),
                SingleInstance = new WindowsSingleInstance(AppPaths.InstanceSuffix),
            };
        }
        if (testRun)
        {
            // Lets the cross-platform parts run in CI on macOS/Linux before their own services exist.
            return new PlatformServices { Secrets = testSecrets!, SingleInstance = new NoSingleInstance() };
        }
        throw new PlatformNotSupportedException("Centinela todavía no funciona en este sistema (llega en la v1.4).");
    }

    /// <summary>Every launch is the first one: for test runs only, until each system has its own.</summary>
    sealed class NoSingleInstance : ISingleInstance
    {
        public bool TryClaim() => true;
        public void ListenForActivation(Action onActivate) { }
    }
}
```

- [ ] **Step 4: La App usa `PlatformServices`**

En `src/Centinela.App/App.xaml.cs`:
- Sustituir el campo `_instance` de la Task 4 por `internal static Centinela.Platform.PlatformServices Platform { get; private set; } = null!;`.
- Al principio de `OnStartup` (tras `base.OnStartup(e);`): `Platform = Centinela.Platform.PlatformServices.Create(Environment.ProcessPath!);`
- `_instance.TryClaim()` → `Platform.SingleInstance.TryClaim()`; `_instance.ListenForActivation(` → `Platform.SingleInstance.ListenForActivation(`.

En `src/Centinela.App/MainWindow.xaml.cs:13`: `readonly CameraStore _store = new(CameraStore.DefaultPath, App.Platform.Secrets);`

En `src/Centinela.App/MainWindow.Tray.cs`: `_autoStart` pasa a `IAutoStart? _autoStart;`, la línea 17 a `_autoStart = App.Platform.AutoStart;`, y `AutoStartAvailable` a `bool AutoStartAvailable => _autoStart is not null;` (deja de ser `static`). En los usos (`_autoStart.Migrate()`, `.Enable()`, `.Disable()`, `.IsEnabled`), que ya están protegidos por `AutoStartAvailable`, usar `_autoStart!.`.

- [ ] **Step 5: Verificar**

Run: `dotnet test tests/Centinela.Platform.Tests -nologo -v q` → pasan.
Run: `dotnet build src/Centinela.App -p:BaseOutputPath=<scratch>\bin\ -nologo -v q` → `0 Errores`, `0 Advertencias`.

- [ ] **Step 6: Commit**

```bash
git add src/Centinela.Platform/PlatformServices.cs tests/Centinela.Platform.Tests/PlatformServicesTests.cs src/Centinela.App/App.xaml.cs src/Centinela.App/MainWindow.xaml.cs src/Centinela.App/MainWindow.Tray.cs
git commit -m "refactor: PlatformServices reúne las piezas del sistema"
```

---

### Task 7: Media por sistema

**Files:**
- Create: `src/Centinela.Media/HardwareAcceleration.cs`
- Create: `src/Centinela.Media/FFmpegLibraries.cs`
- Modify: `src/Centinela.Media/StreamSession.cs:164,418,464,466`
- Modify: `src/Centinela.Media/FFmpegLoader.cs`
- Modify: `src/Centinela.Media/StreamSession.Playout.cs` (reloj de 1 ms solo en Windows)
- Create: `tests/Centinela.Media.Tests/PlatformMediaTests.cs`

**Interfaces:**
- Consumes: `OsKind` (Task 2).
- Produces: `static (AVHWDeviceType Device, AVPixelFormat Format) HardwareAcceleration.For(OsKind os)` y `.Current`; `static string FFmpegLibraries.FileName(OsKind os, string library, int version)`.

- [ ] **Step 1: Escribir las pruebas** (`tests/Centinela.Media.Tests/PlatformMediaTests.cs`)

```csharp
using Centinela.Core.Platform;
using FFmpeg.AutoGen;

namespace Centinela.Media.Tests;

public class PlatformMediaTests
{
    [Theory]
    [InlineData(OsKind.Windows, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, AVPixelFormat.AV_PIX_FMT_D3D11)]
    [InlineData(OsKind.MacOS, AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX)]
    [InlineData(OsKind.Linux, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVPixelFormat.AV_PIX_FMT_VAAPI)]
    public void Each_system_has_its_gpu_decoder(OsKind os, AVHWDeviceType device, AVPixelFormat format) =>
        Assert.Equal((device, format), HardwareAcceleration.For(os));

    [Theory]
    [InlineData(OsKind.Windows, "avcodec-62.dll")]
    [InlineData(OsKind.MacOS, "libavcodec.62.dylib")]
    [InlineData(OsKind.Linux, "libavcodec.so.62")]
    public void Library_file_names_follow_each_system(OsKind os, string expected) =>
        Assert.Equal(expected, FFmpegLibraries.FileName(os, "avcodec", 62));
}
```

- [ ] **Step 2: Ejecutar para ver que fallan**

Run: `dotnet test tests/Centinela.Media.Tests -nologo -v q --filter "FullyQualifiedName~PlatformMediaTests"`
Expected: error de compilación.

- [ ] **Step 3: Implementar**

`src/Centinela.Media/HardwareAcceleration.cs`:

```csharp
using Centinela.Core.Platform;
using FFmpeg.AutoGen;

namespace Centinela.Media;

/// <summary>The GPU decoder of each system and the pixel format its frames come in.</summary>
public static class HardwareAcceleration
{
    public static (AVHWDeviceType Device, AVPixelFormat Format) Current { get; } = For(OsInfo.Current);

    public static (AVHWDeviceType Device, AVPixelFormat Format) For(OsKind os) => os switch
    {
        OsKind.Windows => (AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, AVPixelFormat.AV_PIX_FMT_D3D11),
        OsKind.MacOS => (AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX),
        _ => (AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVPixelFormat.AV_PIX_FMT_VAAPI),
    };
}
```

`src/Centinela.Media/FFmpegLibraries.cs`:

```csharp
using Centinela.Core.Platform;

namespace Centinela.Media;

/// <summary>How each system names a shared FFmpeg library (e.g. avcodec version 62).</summary>
public static class FFmpegLibraries
{
    public static string FileName(OsKind os, string library, int version) => os switch
    {
        OsKind.Windows => $"{library}-{version}.dll",
        OsKind.MacOS => $"lib{library}.{version}.dylib",
        _ => $"lib{library}.so.{version}",
    };
}
```

En `src/Centinela.Media/StreamSession.cs`:
- Línea 164: `AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA` → `HardwareAcceleration.Current.Device`.
- Línea 418 (`GetFormat`): `AVPixelFormat.AV_PIX_FMT_D3D11` → `HardwareAcceleration.Current.Format`.
- Líneas 464 y 466 (`PresentFrame`): `(int)AVPixelFormat.AV_PIX_FMT_D3D11` → `(int)HardwareAcceleration.Current.Format`.

En `src/Centinela.Media/FFmpegLoader.cs`, sustituir la comprobación del archivo por:

```csharp
            var avcodec = Path.Combine(dir, FFmpegLibraries.FileName(OsInfo.Current, "avcodec", ffmpeg.LibraryVersionMap["avcodec"]));
            if (!File.Exists(avcodec))
                throw new FileNotFoundException(
                    $"No se encuentran las librerías de FFmpeg en '{dir}'. Ejecuta el script de tools/ que las descarga y recompila.", avcodec);
```

(con `using Centinela.Core.Platform;`).

En `src/Centinela.Media/StreamSession.Playout.cs`, en `TimerResolution`: `public static void Begin() { if (OperatingSystem.IsWindows()) TimeBeginPeriod(1); }` y `public static void End() { if (OperatingSystem.IsWindows()) TimeEndPeriod(1); }`.

- [ ] **Step 4: (el destino `net10.0` ya se cambió en la Task 1)**

Comprobar que `Directory.Build.props` dice `net10.0` y `Centinela.App.csproj` `net10.0-windows`.

- [ ] **Step 5: Compilar todo sin advertencias y ejecutar todas las pruebas**

Run: `dotnet build Centinela.slnx -p:BaseOutputPath=<scratch>\bin\ -nologo -v q` → `0 Advertencias`, `0 Errores` (si aparece CA1416 en algún uso de Windows fuera de `Centinela.Platform/Windows` o de la App, protegerlo con `OperatingSystem.IsWindows()` o `[SupportedOSPlatform("windows")]`).
Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q`, `dotnet test tests/Centinela.Platform.Tests -nologo -v q`, `dotnet test tests/Centinela.Media.Tests -nologo -v q` → todas pasan (Media con mediamtx y FFmpeg como hasta ahora).

- [ ] **Step 6: Commit**

```bash
git add src/Centinela.Media/HardwareAcceleration.cs src/Centinela.Media/FFmpegLibraries.cs src/Centinela.Media/StreamSession.cs src/Centinela.Media/FFmpegLoader.cs src/Centinela.Media/StreamSession.Playout.cs tests/Centinela.Media.Tests/PlatformMediaTests.cs
git commit -m "refactor: GPU, librerías FFmpeg y reloj de precisión según el sistema"
```

---

### Task 8: Comprobación final de la fase

**Files:** ninguno nuevo (solo verificación; si algo falla, se corrige en el archivo afectado).

- [ ] **Step 1: Nada exclusivo de Windows fuera de su sitio**

Run: `git grep -n "Microsoft.Win32\|ProtectedData\|DllImport\|user32\|System.Windows" -- src/Centinela.Core src/Centinela.Media`
Expected: solo la línea `winmm.dll` de `StreamSession.Playout.cs` (protegida por `OperatingSystem.IsWindows()`); nada más.

- [ ] **Step 2: Suite completa**

Run: `dotnet test tests/Centinela.Core.Tests -nologo -v q`, `dotnet test tests/Centinela.Platform.Tests -nologo -v q`, `dotnet test tests/Centinela.Media.Tests -nologo -v q` → todas pasan.

- [ ] **Step 3: Compatibilidad con la 1.3**

Con `CENTINELA_DATA_DIR` apuntando a una carpeta temporal: guardar con `CameraStore` + `DpapiSecretStore` una cámara con contraseña y comprobar que el valor escrito en `cameras.json` no lleva `:` (lo mismo que escribe la 1.3). Esto ya lo cubre `DpapiSecretStoreTests.Round_trips_without_a_prefix_so_centinela_1_3_can_still_read_it`; confirmar que pasa.

- [ ] **Step 4: Prueba del usuario**

Pedir al usuario que cierre su Centinela y ejecute la rama (`dotnet run --project src/Centinela.App`): mismas cámaras, contraseñas, bandeja y «Arrancar con Windows» que antes. Sin cambios visibles.
