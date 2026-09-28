using Centinela.Core;

namespace Centinela.App;

/// <summary>
/// Turns session errors into log entries and throttled toasts. UI thread only.
/// Every error is logged; <paramref name="decide"/> (the notification gate) says whether a throttled notice is shown
/// and whether it sounds.
/// </summary>
public sealed class ErrorCenter(ErrorLog log, Func<NoticeKind, Camera, NoticeDecision> decide)
{
    readonly ErrorNotificationPolicy _policy = new();

    public event Action<Toast>? ToastRequested;
    public event Action<NoticeKind>? SoundRequested;
    public event Action<int>? UnreadChanged;
    public int Unread { get; private set; }

    public void Report(Camera camera, StreamError error)
    {
        var text = Translate(camera, error.Kind);
        log.Add(new ErrorLogEntry(DateTime.Now, camera.Name, text.Short, text.Title, error.Detail));
        Unread++;
        UnreadChanged?.Invoke(Unread);
        if (!_policy.ShouldNotify(camera.Id, error.Kind)) return;
        var decision = decide(NoticeKind.ConnectionLost, camera);
        if (decision.Show) ToastRequested?.Invoke(new Toast(text.Title, text.Advice, ToastStyle.Error));
        if (decision.PlaySound) SoundRequested?.Invoke(NoticeKind.ConnectionLost);
    }

    public void Playing(Camera camera)
    {
        if (!_policy.OnPlaying(camera.Id)) return;
        log.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Recuperada", $"{camera.Name}: conexión recuperada", ""));
        if (decide(NoticeKind.ConnectionRecovered, camera).Show)
            ToastRequested?.Invoke(new Toast($"✓ {camera.Name}: conexión recuperada", "", ToastStyle.Recovery));
    }

    public void Forget(Guid cameraId) => _policy.Forget(cameraId);

    public void MarkRead()
    {
        Unread = 0;
        UnreadChanged?.Invoke(0);
    }

    /// <summary>An auth failure on a camera with no stored password means the password is missing (e.g. after importing a copy without passwords).</summary>
    public static TranslatedError Translate(Camera camera, StreamErrorKind kind) =>
        kind == StreamErrorKind.AuthFailed && camera.Password.Length == 0
            ? new TranslatedError("Falta contraseña", $"{camera.Name}: falta la contraseña", "Pulsa «Editar» en la cámara y escribe su contraseña.")
            : ErrorTranslator.Translate(kind, camera.Brand, camera.Name);
}
