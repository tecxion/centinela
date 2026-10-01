using System.Runtime.InteropServices;
using Centinela.Core.Platform;

namespace Centinela.Core;

/// <summary>The name of the release file published for each system, so the user knows which one to download.</summary>
public static class UpdateAssets
{
    /// <summary>
    /// Windows x64 .exe, macOS arm64/Intel .dmg, Linux x64 .deb; the version as major.minor.patch.
    /// A system or architecture with no package throws <see cref="PlatformNotSupportedException"/>.
    /// </summary>
    public static string FileName(Version version, OsKind os, Architecture arch)
    {
        var v = UpdateChecker.Normalize(version);
        return (os, arch) switch
        {
            (OsKind.Windows, Architecture.X64) => $"Centinela-{v}-windows-x64.exe",
            (OsKind.MacOS, Architecture.Arm64) => $"Centinela-{v}-macos-arm64.dmg",
            (OsKind.MacOS, Architecture.X64) => $"Centinela-{v}-macos-intel.dmg",
            (OsKind.Linux, Architecture.X64) => $"centinela_{v}_amd64.deb",
            _ => throw new PlatformNotSupportedException($"No hay paquete de Centinela para {os} {arch}."),
        };
    }
}
