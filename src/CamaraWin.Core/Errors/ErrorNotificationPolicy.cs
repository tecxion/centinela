namespace CamaraWin.Core;

/// <summary>
/// Decides when a connection error deserves a toast: once per (camera, kind) until the camera plays
/// again; transient kinds only after <see cref="RepeatThreshold"/> consecutive failures. Not thread-safe.
/// </summary>
public sealed class ErrorNotificationPolicy
{
    public const int RepeatThreshold = 3;

    readonly Dictionary<Guid, CameraState> _states = [];

    sealed class CameraState
    {
        public int Failures;
        public readonly HashSet<StreamErrorKind> Notified = [];
    }

    public bool ShouldNotify(Guid cameraId, StreamErrorKind kind)
    {
        if (!_states.TryGetValue(cameraId, out var state)) _states[cameraId] = state = new CameraState();
        state.Failures++;
        if (state.Notified.Contains(kind)) return false;
        var immediate = kind is StreamErrorKind.AuthFailed or StreamErrorKind.NotFound or StreamErrorKind.CameraBusy;
        if (!immediate && state.Failures < RepeatThreshold) return false;
        state.Notified.Add(kind);
        return true;
    }

    /// <summary>Resets the camera. True when a notice had been shown (so a "recovered" notice makes sense).</summary>
    public bool OnPlaying(Guid cameraId) => _states.Remove(cameraId, out var state) && state.Notified.Count > 0;

    public void Forget(Guid cameraId) => _states.Remove(cameraId);
}
