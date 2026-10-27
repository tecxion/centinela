namespace Centinela.Core;

public static class UpdatePolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Enabled and not checked in the last 24 h (a last check in the future — clock changed — does not block).</summary>
    public static bool ShouldAutoCheck(AppSettings settings, DateTimeOffset now) =>
        settings.CheckUpdatesOnStartup
        && (settings.LastUpdateCheck is not { } last || last > now || now - last >= Interval);

    public static bool ShouldNotify(UpdateResult result, AppSettings settings) =>
        result is { Status: UpdateStatus.UpdateAvailable, Latest: { } latest } && latest.ToString() != settings.SkippedVersion;

    /// <summary>Only an answer from GitHub resets the daily timer; failures are retried next start.</summary>
    public static bool CountsAsChecked(UpdateResult result) => result.Status != UpdateStatus.Failed;
}
