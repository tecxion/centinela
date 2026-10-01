using System.Runtime.InteropServices;
using Centinela.Core;
using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

public class UpdateAssetsTests
{
    static readonly Version V = new(1, 4, 2);

    [Fact]
    public void Each_system_gets_its_own_package()
    {
        Assert.Equal("Centinela-1.4.2-windows-x64.exe", UpdateAssets.FileName(V, OsKind.Windows, Architecture.X64));
        Assert.Equal("Centinela-1.4.2-macos-arm64.dmg", UpdateAssets.FileName(V, OsKind.MacOS, Architecture.Arm64));
        Assert.Equal("Centinela-1.4.2-macos-intel.dmg", UpdateAssets.FileName(V, OsKind.MacOS, Architecture.X64));
        Assert.Equal("centinela_1.4.2_amd64.deb", UpdateAssets.FileName(V, OsKind.Linux, Architecture.X64));
    }

    [Fact]
    public void Version_is_written_as_major_minor_patch()
    {
        Assert.Equal("Centinela-1.4.0-windows-x64.exe", UpdateAssets.FileName(new Version(1, 4), OsKind.Windows, Architecture.X64));
        Assert.Equal("centinela_1.4.0_amd64.deb", UpdateAssets.FileName(new Version(1, 4), OsKind.Linux, Architecture.X64));
    }

    [Theory]
    [InlineData(OsKind.Linux, Architecture.Arm64)]
    [InlineData(OsKind.Windows, Architecture.Arm64)]
    [InlineData(OsKind.Windows, Architecture.X86)]
    [InlineData(OsKind.MacOS, Architecture.X86)]
    public void Systems_without_a_package_throw(OsKind os, Architecture arch)
    {
        Assert.Throws<PlatformNotSupportedException>(() => UpdateAssets.FileName(V, os, arch));
    }
}
