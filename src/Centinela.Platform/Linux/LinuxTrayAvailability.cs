using System.Runtime.Versioning;
using Centinela.Core.Platform;

namespace Centinela.Platform.Linux;

/// <summary>
/// A tray icon can show when some process owns org.kde.StatusNotifierWatcher on the session bus: Avalonia's
/// TrayIcon registers there on Linux (on GNOME, through the AppIndicator extension). False without a session bus.
/// May wait for the bus (at most a few seconds).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxTrayAvailability(SessionBus bus) : ITrayAvailability
{
    const string Watcher = "org.kde.StatusNotifierWatcher";

    public bool IsAvailable => bus.HasOwner(Watcher);
}
