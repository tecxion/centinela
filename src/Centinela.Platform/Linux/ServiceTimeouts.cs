namespace Centinela.Platform.Linux;

/// <summary>
/// Remembers that the Secret Service did not answer a call in time (not a prompt the user turned down), so the
/// following operations give false/null at once instead of waiting again; cleared as soon as an operation gets its
/// replies (e.g. one already under way). For this session of Centinela.
/// </summary>
internal sealed class ServiceTimeouts
{
    volatile bool _silent;

    /// <summary>The service timed out and has not answered since.</summary>
    public bool Silent => _silent;

    /// <summary>
    /// Runs one operation: a <see cref="TimeoutException"/> (a call left unanswered) marks the service silent; a
    /// completed operation, whatever its result, clears it. Other failures (an error reply, a cancellation) change nothing.
    /// </summary>
    public async Task<T> Watch<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            _silent = false;
            return result;
        }
        catch (TimeoutException)
        {
            _silent = true;
            throw;
        }
    }
}
