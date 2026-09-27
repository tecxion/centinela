using CamaraWin.Core;

namespace CamaraWin.App;

/// <summary>Turns session errors into log entries and throttled toasts. UI thread only.</summary>
public sealed class ErrorCenter(ErrorLog log)
{
    readonly ErrorNotificationPolicy _policy = new();

    public event Action<Toast>? ToastRequested;
    public event Action<int>? UnreadChanged;
    public int Unread { get; private set; }

    public void Report(Camera camera, StreamError error)
    {
        var text = Translate(camera, error.Kind);
        log.Add(new ErrorLogEntry(DateTime.Now, camera.Name, text.Short, text.Title, error.Detail));
        Unread++;
        UnreadChanged?.Invoke(Unread);
        if (_policy.ShouldNotify(camera.Id, error.Kind)) ToastRequested?.Invoke(new Toast(text.Title, text.Advice, false));
    }

    public void Playing(Camera camera)
    {
        if (!_policy.OnPlaying(camera.Id)) return;
        log.Add(new ErrorLogEntry(DateTime.Now, camera.Name, "Recuperada", $"{camera.Name}: conexión recuperada", ""));
        ToastRequested?.Invoke(new Toast($"✓ {camera.Name}: conexión recuperada", "", true));
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
