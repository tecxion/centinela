using Centinela.Core;

namespace Centinela.Desktop.Services.Errors;

static class ErrorText
{
    /// <summary>An auth failure on a camera with no stored password means the password is missing (e.g. a copy imported without passwords).</summary>
    public static TranslatedError Translate(Camera camera, StreamErrorKind kind) =>
        kind == StreamErrorKind.AuthFailed && camera.Password.Length == 0
            ? new TranslatedError("Falta contraseña", $"{camera.Name}: falta la contraseña", "Pulsa «Editar» en la cámara y escribe su contraseña.")
            : ErrorTranslator.Translate(kind, camera.Brand, camera.Name);
}
