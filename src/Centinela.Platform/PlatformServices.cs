using System.Runtime.Versioning;
using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Platform.Linux;
using Centinela.Platform.Unix;
using Centinela.Platform.Windows;

namespace Centinela.Platform;

/// <summary>
/// The system pieces for this process. With CENTINELA_DATA_DIR (tests, smoke runs) nothing real is touched:
/// passwords go to a key file in that folder and there is no auto start. macOS arrives in phase 4.
/// </summary>
public sealed class PlatformServices
{
    public required ISecretStore Secrets { get; init; }
    /// <summary>Null when starting with the system is not offered (test data folder, or not supported here).</summary>
    public IAutoStart? AutoStart { get; init; }
    public required ISingleInstance SingleInstance { get; init; }
    public required IFileManager Files { get; init; }
    /// <summary>Makes the tray icon (on the UI thread); null where this system's tray is not there yet (phases 3–4).</summary>
    public Func<ITray>? CreateTray { get; init; }
    /// <summary>System notifications outside the tray (Linux); null on Windows, where the tray shows them.</summary>
    public INotifier? Notifier { get; init; }
    /// <summary>
    /// Whether a tray icon (Avalonia's) can show, where <see cref="CreateTray"/> is null; null on Windows. Its check
    /// may wait for the session bus: read it once, off the UI thread.
    /// </summary>
    public ITrayAvailability? TrayAvailability { get; init; }

    /// <summary>
    /// The passwords went to (or came from) the encrypted file because there is no keychain (Linux): the UI says so
    /// once. Read after loading or saving the cameras.
    /// </summary>
    public bool SecretsUseFileFallback => Secrets is LinuxSecretStore { UsesFileFallback: true };

    /// <param name="exePath">The installed program, which starting with the system runs.</param>
    /// <param name="ui">
    /// Where a click on a system notification runs (Linux). It is only posted to once the app runs, so it may be
    /// created before the UI exists.
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
        if (testRun)
        {
            // Lets the cross-platform parts run in CI on macOS before its own services exist.
            return new PlatformServices
            {
                Secrets = testSecrets!,
                SingleInstance = new NoSingleInstance(),
                Files = new ShellFileManager(),
            };
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
            Secrets = testRun ? file : new LinuxSecretStore(new SecretServiceClient(bus), file),
            AutoStart = testRun ? null : new LinuxAutoStart(LinuxAutoStart.DefaultDirectory(env, home), exePath),
            SingleInstance = new UnixSingleInstance(UnixSingleInstance.PathFor(env("XDG_RUNTIME_DIR"), dataDir, AppPaths.InstanceSuffix)),
            Files = new ShellFileManager(),
            Notifier = new LinuxNotifier(bus, ui),
            TrayAvailability = new LinuxTrayAvailability(bus),
        };
    }

    // A method group, not a lambda: the platform analyzer does not carry the IsWindows() guard into a lambda.
    [SupportedOSPlatform("windows")]
    static ITray NewWindowsTray() => new WindowsTray();

    /// <summary>Every launch is the first one: for test runs only, until each system has its own.</summary>
    sealed class NoSingleInstance : ISingleInstance
    {
        public bool TryClaim() => true;
        public void ListenForActivation(Action onActivate) { }
    }
}
