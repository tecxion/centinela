using System.Runtime.Versioning;
using Centinela.Core;
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
            var services = PlatformServices.Create(@"C:\Apps\Centinela\Centinela.exe");
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
            var services = PlatformServices.Create("/opt/centinela/Centinela");
            Assert.IsType<FileSecretStore>(services.Secrets);
            Assert.Null(services.AutoStart);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, original);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
