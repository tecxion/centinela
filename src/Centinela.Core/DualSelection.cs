namespace Centinela.Core;

/// <summary>The two big cameras of the Dual layout (left, right) and which one a click replaces next.</summary>
public sealed record DualState(IReadOnlyList<Guid> Ids, int NextReplace);

public static class DualSelection
{
    /// <summary>A click on a thumbnail replaces the big camera that has gone longest without changing.</summary>
    public static DualState Click(DualState state, Guid cameraId)
    {
        if (state.Ids.Contains(cameraId)) return state;
        if (state.Ids.Count < 2) return new DualState([.. state.Ids, cameraId], state.NextReplace);
        var ids = state.Ids.ToList();
        ids[state.NextReplace] = cameraId;
        return new DualState(ids, 1 - state.NextReplace);
    }

    /// <summary>A drop onto big camera <paramref name="targetIndex"/> puts the camera there (swapping if it was the other big one).</summary>
    public static DualState Drop(DualState state, Guid cameraId, int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= state.Ids.Count || state.Ids[targetIndex] == cameraId) return state;
        var ids = state.Ids.ToList();
        var from = ids.IndexOf(cameraId);
        if (from >= 0) (ids[from], ids[targetIndex]) = (ids[targetIndex], ids[from]);
        else ids[targetIndex] = cameraId;
        return new DualState(ids, 1 - targetIndex);
    }
}
