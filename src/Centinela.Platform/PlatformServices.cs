using System.Runtime.Versioning;
using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Platform.Linux;
using Centinela.Platform.MacOS;
using Centinela.Platform.Unix;
using Centinela.Platform.Windows;

namespace Centinela.Platform;

/// <summary>
/// The system pieces for this process. With CENTINELA_DATA_DIR (tests, smoke runs) nothing real is touched:
/// passwords go to a key file in that folder and there is no auto start. Windows, Linux and macOS only.
/// </summary>
public sealed class PlatformServices
{
    public required ISecretStore Secrets { get; init; }
    /// <summary>Null when starting with the system is not offered (test data folder, or not supported here).</summary>
    public IAutoStart? AutoStart { get; init; }
    public required ISingleInstance SingleInstance { get; init; }
    public required IFileManager Files { get; init; }
    /// <summary>Makes the tray icon (on the UI thread); null where the UI makes Avalonia's (Linux, macOS).</summary>
    public Func<ITray>? CreateTray { get; init; }
    /// <summary>
    /// System notifications outside the tray (Linux, macOS); null on Windows, where the tray shows them.
    /// </summary>
    public INotifier? Notifier { get; init; }
    /// <summary>
    /// Whether a tray icon (Avalonia's) can show, where <see cref="CreateTray"/> is null; null on Windows. Its check
    /// may wait for the session bus: read it once, off the UI thread.
    /// </summary>
    public ITrayAvailability? TrayAvailability { get; init; }

    /// <summary>
    /// The passwords went to (or came from) the encrypted file because there is no keychain (Linux, macOS): the UI
    /// says so once. Read after loading or saving the cameras.
    /// </summary>
    public bool SecretsUseFileFallback => Secrets is KeychainSecretStore { UsesFileFallback: true };

    /// <param name="exePath">The installed program, which starting with the system runs.</param>
    /// <param name="ui">
    /// Where a click on a system notification runs (Linux, macOS). It is only posted to once the app runs, so it may
    /// be created before the UI exists.
    /// </param>
    public static PlatformServices Create(string exePath, SynchronizationContext ui)
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
                Files = new ExplorerFileManager(),
                // Also for test runs: the tray touches no data.
                CreateTray = NewWindowsTray,
            };
        }
        if (OperatingSystem.IsLinux())
        {
            return ForLinux(exePath, testRun, AppPaths.DataDirectory, Environment.GetEnvironmentVariable,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ui);
        }
        if (OperatingSystem.IsMacOS())
        {
            return ForMacOS(exePath, testRun, AppPaths.DataDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ui);
        }
        throw new PlatformNotSupportedException("Centinela todavía no está disponible en este sistema.");
    }

    /// <summary>
    /// Linux: the Secret Service keychain, falling back to the encrypted file; an XDG autostart entry; a Unix socket
    /// for the single instance; xdg-open; notifications and the tray check over one session bus. Avalonia's tray is
    /// made by the UI (<see cref="CreateTray"/> stays null). With a test data folder only that folder's key file and
    /// no auto start. Nothing is connected or written here.
    /// </summary>
    /// <remarks>
    /// The keychain client is always given: without a session bus each of its calls fails at once (the bus is
    /// connected once, on first use) and the store uses its file, so checking for a bus here would only delay startup.
    /// </remarks>
    [SupportedOSPlatform("linux")]
    internal static PlatformServices ForLinux(string exePath, bool testRun, string dataDir, Func<string, string?> env,
        string home, SynchronizationContext ui)
    {
        var bus = new SessionBus();
        var file = new FileSecretStore(Path.Combine(dataDir, "secret.key"));
        return new PlatformServices
        {
            Secrets = testRun ? file : new KeychainSecretStore(SecretPrefix.SecretService, new SecretServiceClient(bus), file),
            AutoStart = testRun ? null : new LinuxAutoStart(LinuxAutoStart.DefaultDirectory(env, home), exePath),
            SingleInstance = new UnixSingleInstance(UnixSingleInstance.PathFor(env("XDG_RUNTIME_DIR"), dataDir, AppPaths.InstanceSuffix)),
            Files = new ShellFileManager(),
            Notifier = new LinuxNotifier(bus, ui),
            TrayAvailability = new LinuxTrayAvailability(bus),
        };
    }

    /// <summary>
    /// macOS: the Keychain, falling back to the encrypted file; a launchd agent; a Unix socket in the data folder
    /// (there is no XDG_RUNTIME_DIR) for the single instance; open; UserNotifications when run from the .app. The
    /// menu bar always has room for Avalonia's tray icon, which the UI makes. With a test data folder only that
    /// folder's key file and no auto start. Nothing is opened or written here.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal static PlatformServices ForMacOS(string exePath, bool testRun, string dataDir, string home,
        SynchronizationContext ui)
    {
        var file = new FileSecretStore(Path.Combine(dataDir, "secret.key"));
        return new PlatformServices
        {
            Secrets = testRun ? file : new KeychainSecretStore(SecretPrefix.Keychain, new MacKeychain(), file),
            AutoStart = testRun ? null : new MacAutoStart(MacAutoStart.DefaultDirectory(home), exePath),
            SingleInstance = new UnixSingleInstance(UnixSingleInstance.PathFor(null, dataDir, AppPaths.InstanceSuffix)),
            Files = new ShellFileManager(),
            // Also for test runs: notifications touch no data.
            Notifier = MacNotifier.TryCreate(ui),
            TrayAvailability = new MenuBar(),
        };
    }

    // A method group, not a lambda: the platform analyzer does not carry the IsWindows() guard into a lambda.
    [SupportedOSPlatform("windows")]
    static ITray NewWindowsTray() => new WindowsTray();

    /// <summary>macOS always has a menu bar for the tray icon.</summary>
    sealed class MenuBar : ITrayAvailability
    {
        public bool IsAvailable => true;
    }
}
