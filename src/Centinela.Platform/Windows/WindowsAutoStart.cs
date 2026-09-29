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
