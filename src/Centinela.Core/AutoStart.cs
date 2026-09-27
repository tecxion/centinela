using Microsoft.Win32;

namespace Centinela.Core;

public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run — no admin rights needed.</summary>
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

public sealed class AutoStart(IRunKey key, string exePath)
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

    /// <summary>If the pre-rename entry exists, replaces it with one for this exe. Returns whether it did.</summary>
    public bool MigrateLegacy()
    {
        if (key.Get(LegacyValueName) is null) return false;
        Enable();
        return true;
    }
}
