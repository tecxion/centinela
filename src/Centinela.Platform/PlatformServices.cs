using System.Runtime.Versioning;
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
    public required IFileManager Files { get; init; }
    /// <summary>Makes the tray icon (on the UI thread); null where this system's tray is not there yet (phases 3–4).</summary>
    public Func<ITray>? CreateTray { get; init; }

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
                Files = new ExplorerFileManager(),
                // Also for test runs: the tray touches no data.
                CreateTray = NewWindowsTray,
            };
        }
        if (testRun)
        {
            // Lets the cross-platform parts run in CI on macOS/Linux before their own services exist.
            return new PlatformServices
            {
                Secrets = testSecrets!,
                SingleInstance = new NoSingleInstance(),
                Files = new ShellFileManager(),
            };
        }
        throw new PlatformNotSupportedException("Centinela todavía no funciona en este sistema (llega en la v1.4).");
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
