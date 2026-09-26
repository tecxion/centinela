namespace CamaraWin.Core;

public sealed record TranslatedError(string Short, string Title, string Advice);

public static class ErrorTranslator
{
    public static TranslatedError Translate(StreamErrorKind kind, Brand brand, string cameraName) => kind switch
    {
        StreamErrorKind.AuthFailed => new("Contraseña incorrecta", $"{cameraName}: usuario o contraseña incorrectos", brand switch
        {
            Brand.Tapo => "Usa la «Cuenta de cámara» que creaste en la app Tapo (Ajustes avanzados), no tu cuenta de TP-Link.",
            Brand.Imou => "El usuario es «admin» y la contraseña es el código de seguridad de la pegatina de la cámara.",
            _ => "Revisa el usuario y la contraseña de la cámara.",
        }),
        StreamErrorKind.Unreachable => new("Sin conexión", $"{cameraName}: no se puede conectar con la cámara",
            "Comprueba que la cámara está encendida y en la misma red, y que su IP no ha cambiado (conviene reservarla en el router)."),
        StreamErrorKind.NotFound => new("Vídeo no disponible", $"{cameraName}: la cámara no ofrece el vídeo pedido", brand switch
        {
            Brand.Imou => "Activa RTSP/ONVIF en la app Imou (ajustes de la cámara) y vuelve a intentarlo.",
            Brand.Tapo => "Crea la «Cuenta de cámara» en la app Tapo (Ajustes avanzados); sin ella no hay vídeo RTSP.",
            _ => "Revisa la URL RTSP en «Editar › Avanzado».",
        }),
        StreamErrorKind.CameraBusy => new("Cámara ocupada", $"{cameraName}: la cámara no acepta más conexiones",
            "Hay demasiadas conexiones a la vez: cierra la app oficial, la pantalla completa o una grabación de esta cámara."),
        StreamErrorKind.ServerError => new("Error de la cámara", $"{cameraName}: la cámara devolvió un error interno",
            "Reinicia la cámara. Si continúa, actualiza su firmware desde la app oficial."),
        StreamErrorKind.Stalled => new("Imagen congelada", $"{cameraName}: la cámara dejó de enviar vídeo",
            "Se reconectará sola. Si se repite, revisa la cobertura Wi-Fi de la cámara."),
        _ => new("Error de conexión", $"{cameraName}: error de conexión",
            "Consulta el detalle técnico en el registro (Registro, en la barra inferior)."),
    };
}
