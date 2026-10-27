using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Info;

/// <summary>The manual's paragraphs that depend on the system: tray or menu bar, volume, folders and logs.</summary>
sealed record ManualSystemTexts(string Sound, string Folders, string TrayHeading, string Tray, string MotionNotice,
    string Logs, string Documents)
{
    const string MotionLimit = "Como mucho sale un aviso por cámara cada tiempo de espera.";

    public static ManualSystemTexts For(OsKind os) => os switch
    {
        OsKind.Windows => Windows,
        OsKind.MacOS => MacOS,
        _ => Linux,
    };

    /// <summary>Exactly the WPF app's texts.</summary>
    static readonly ManualSystemTexts Windows = new(
        Sound: "El sonido se para al ocultar Centinela en la bandeja o al cerrar la pantalla completa. El volumen se ajusta en el mezclador de volumen de Windows.",
        Folders: @"Las grabaciones se guardan en Vídeos\Centinela y las capturas en Imágenes\Centinela.",
        TrayHeading: "Bandeja del sistema",
        Tray: "La X de la ventana la oculta en la bandeja del sistema (junto al reloj) y las grabaciones continúan. «Salir» cierra Centinela del todo.",
        MotionNotice: $"Sigue vigilando con Centinela oculta en la bandeja. Si la ventana está minimizada o en la bandeja, sale un aviso junto al reloj; haz clic en él para abrir la ventana. {MotionLimit}",
        Logs: @"%AppData%\Centinela\logs",
        Documents: @"Documentos\Centinela");

    static readonly ManualSystemTexts Linux = new(
        Sound: "El sonido se para al ocultar Centinela en la bandeja o al cerrar la pantalla completa. El volumen se ajusta en los ajustes de sonido del sistema.",
        Folders: "Las grabaciones se guardan en Vídeos/Centinela y las capturas en Imágenes/Centinela.",
        TrayHeading: "Bandeja del sistema",
        Tray: "La X de la ventana la oculta en la bandeja (el icono de la barra superior) y las grabaciones continúan. Si tu escritorio no tiene bandeja, la X minimiza la ventana. «Salir» cierra Centinela del todo.",
        MotionNotice: $"Sigue vigilando con Centinela oculta en la bandeja. Si la ventana está minimizada o en la bandeja, sale una notificación del sistema; haz clic en ella para abrir la ventana. {MotionLimit}",
        Logs: "~/.local/share/centinela/logs",
        Documents: "Documentos/Centinela");

    static readonly ManualSystemTexts MacOS = new(
        Sound: "El sonido se para al ocultar Centinela en la barra de menús o al cerrar la pantalla completa. El volumen se ajusta en los ajustes de sonido del sistema.",
        Folders: "Las grabaciones se guardan en Películas/Centinela y las capturas en Imágenes/Centinela.",
        TrayHeading: "Barra de menús",
        Tray: "El botón rojo de la ventana la oculta en la barra de menús (el icono de arriba a la derecha) y las grabaciones continúan. Para volver a abrirla, usa ese icono o haz clic en Centinela en el Dock. «Salir» (o ⌘Q) cierra Centinela del todo.",
        MotionNotice: $"Sigue vigilando con Centinela oculta en la barra de menús. Si la ventana está minimizada o en la barra de menús, sale una notificación del sistema; haz clic en ella para abrir la ventana. {MotionLimit}",
        Logs: "~/Library/Application Support/Centinela/logs",
        Documents: "Documentos/Centinela");
}
