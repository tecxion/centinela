using System.Runtime.Versioning;
using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Platform.Linux;
using Centinela.Platform.MacOS;
using Centinela.Platform.Unix;
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

public class PlatformServicesTests
{
    [SkippableFact]
    [SupportedOSPlatform("windows")]
    public void Windows_gets_dpapi_the_run_key_and_the_mutex()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "servicios de Windows");
        var original = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
            var services = PlatformServices.Create(@"C:\Apps\Centinela\Centinela.exe", new SynchronizationContext());
            Assert.IsType<DpapiSecretStore>(services.Secrets);
            Assert.IsType<WindowsAutoStart>(services.AutoStart);
            Assert.IsType<WindowsSingleInstance>(services.SingleInstance);
            // Never invoked here: it would put an icon in the real tray.
            Assert.NotNull(services.CreateTray);
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
            var services = PlatformServices.Create("/opt/centinela/Centinela", new SynchronizationContext());
            Assert.IsType<FileSecretStore>(services.Secrets);
            Assert.Null(services.AutoStart);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, original);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    static string? LinuxEnv(string name) => name switch
    {
        "XDG_RUNTIME_DIR" => "/run/user/1000",
        _ => null,
    };

    // Only builds the pieces: nothing here connects to a bus, binds a socket or writes a file.
    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_gets_the_keychain_with_file_fallback_xdg_autostart_the_unix_socket_and_its_notifications()
    {
        var services = PlatformServices.ForLinux("/opt/centinela/Centinela", testRun: false, "/home/ana/.local/share/centinela",
            LinuxEnv, "/home/ana", new SynchronizationContext());
        Assert.IsType<KeychainSecretStore>(services.Secrets);
        Assert.IsType<LinuxAutoStart>(services.AutoStart);
        Assert.IsType<UnixSingleInstance>(services.SingleInstance);
        Assert.IsType<ShellFileManager>(services.Files);
        Assert.IsType<LinuxNotifier>(services.Notifier);
        Assert.IsType<LinuxTrayAvailability>(services.TrayAvailability);
        Assert.Null(services.CreateTray);
        Assert.False(services.SecretsUseFileFallback);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Linux_with_a_test_data_folder_gets_only_the_file_store_and_no_auto_start()
    {
        var services = PlatformServices.ForLinux("/opt/centinela/Centinela", testRun: true, "/tmp/centinela-pruebas",
            LinuxEnv, "/home/ana", new SynchronizationContext());
        Assert.IsType<FileSecretStore>(services.Secrets);
        Assert.Null(services.AutoStart);
        Assert.IsType<UnixSingleInstance>(services.SingleInstance);
        Assert.Null(services.CreateTray);
    }

    const string MacExe = "/Applications/Centinela.app/Contents/MacOS/Centinela";

    // Only builds the pieces: nothing here opens the Keychain, writes the agent or binds a socket.
    [Fact]
    [SupportedOSPlatform("macos")]
    public void MacOS_gets_the_keychain_with_file_fallback_a_launch_agent_the_unix_socket_and_the_menu_bar()
    {
        var services = PlatformServices.ForMacOS(MacExe, testRun: false, "/Users/ana/Library/Application Support/Centinela",
            "/Users/ana", new SynchronizationContext());
        Assert.Equal(SecretPrefix.Keychain, Assert.IsType<KeychainSecretStore>(services.Secrets).Prefix);
        Assert.IsType<MacAutoStart>(services.AutoStart);
        Assert.IsType<UnixSingleInstance>(services.SingleInstance);
        Assert.IsType<ShellFileManager>(services.Files);
        // No libobjc on Windows, no app bundle for the test host on macOS.
        Assert.Null(services.Notifier);
        Assert.True(services.TrayAvailability!.IsAvailable);
        Assert.Null(services.CreateTray);
        Assert.False(services.SecretsUseFileFallback);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void MacOS_with_a_test_data_folder_gets_only_the_file_store_and_no_auto_start()
    {
        var services = PlatformServices.ForMacOS(MacExe, testRun: true, "/tmp/centinela-pruebas", "/Users/ana",
            new SynchronizationContext());
        Assert.IsType<FileSecretStore>(services.Secrets);
        Assert.Null(services.AutoStart);
        Assert.IsType<UnixSingleInstance>(services.SingleInstance);
    }

    [Fact]
    public void Secrets_use_the_file_fallback_once_the_keychain_store_used_its_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "centinela-svc-" + Guid.NewGuid());
        try
        {
            var store = new KeychainSecretStore(SecretPrefix.SecretService, backend: null, new FileSecretStore(Path.Combine(dir, "secret.key")));
            var services = new PlatformServices { Secrets = store, SingleInstance = new UnixSingleInstance(Path.Combine(dir, "s.sock")), Files = new ShellFileManager() };
            Assert.False(services.SecretsUseFileFallback);
            store.Protect(Guid.NewGuid(), "clave");
            Assert.True(services.SecretsUseFileFallback);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Other_stores_never_use_the_file_fallback()
    {
        var services = new PlatformServices
        {
            Secrets = new FileSecretStore(Path.Combine(Path.GetTempPath(), "centinela-none", "secret.key")),
            SingleInstance = new UnixSingleInstance("unused.sock"),
            Files = new ShellFileManager(),
        };
        Assert.False(services.SecretsUseFileFallback);
    }
}
