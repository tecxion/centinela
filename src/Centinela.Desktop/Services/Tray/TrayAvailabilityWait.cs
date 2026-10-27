namespace Centinela.Desktop.Services.Tray;

/// <summary>The UI thread waits for the startup tray check at most a few seconds; past that, there is no tray.</summary>
static class TrayAvailabilityWait
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    public static bool Get(Task<bool> check, TimeSpan timeout)
    {
        try { return check.Wait(timeout) && check.Result; }
        catch (Exception) { return false; }
    }
}
