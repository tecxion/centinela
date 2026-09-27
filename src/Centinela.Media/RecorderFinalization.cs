namespace Centinela.Media;

/// <summary>A recording file could not be written or finalized correctly.</summary>
public sealed class RecordingException(string message) : Exception(message);

public static class RecorderFinalization
{
    /// <summary>
    /// Waits for <paramref name="initial"/> and for whatever <paramref name="pending"/> reports afterwards (a recorder
    /// being created concurrently may register late), then faults with every recorder error, if any.
    /// Each entry is the task that completes once the file is closed and the recorder's error getter.
    /// </summary>
    public static async Task WhenAllFinalizedAsync(
        IReadOnlyList<(Task Finished, Func<string?> Error)> initial,
        Func<IReadOnlyList<(Task Finished, Func<string?> Error)>> pending)
    {
        var seen = new HashSet<Task>();
        var errors = new List<string>();
        var batch = initial;
        while (batch.Count > 0)
        {
            await Task.WhenAll(batch.Select(entry => entry.Finished)).ConfigureAwait(false);
            foreach (var (finished, error) in batch)
                if (seen.Add(finished) && error() is { } message && !errors.Contains(message)) errors.Add(message);
            batch = [.. pending().Where(entry => !seen.Contains(entry.Finished))];
        }
        if (errors.Count > 0) throw new RecordingException(string.Join(" ", errors));
    }
}
