using System.Runtime.Versioning;
using Centinela.Platform.Windows;

namespace Centinela.Platform.Tests;

// The attribute only tells the platform analyzer (CA1416) that Skip.IfNot guards every test; xUnit ignores it.
[SupportedOSPlatform("windows")]
public class WindowsSingleInstanceTests
{
    [SkippableFact]
    public void Second_claim_fails_and_wakes_the_first()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "mutex con nombre de Windows");
        var suffix = ".test-" + Guid.NewGuid().ToString("N");
        using var first = new WindowsSingleInstance(suffix);
        Assert.True(first.TryClaim());
        using var woken = new ManualResetEventSlim();
        first.ListenForActivation(woken.Set);

        // A second claimant must run on another thread: a Windows mutex is re-entrant for its owner thread.
        var second = Task.Run(() =>
        {
            using var other = new WindowsSingleInstance(suffix);
            return other.TryClaim();
        });
        Assert.False(second.Result);
        Assert.True(woken.Wait(TimeSpan.FromSeconds(5)));
    }

    [SkippableFact]
    public void Repeated_claim_keeps_the_first_result()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "mutex con nombre de Windows");
        var suffix = ".test-" + Guid.NewGuid().ToString("N");
        using var instance = new WindowsSingleInstance(suffix);
        Assert.True(instance.TryClaim());
        Assert.True(instance.TryClaim());
    }
}
