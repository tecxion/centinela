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
