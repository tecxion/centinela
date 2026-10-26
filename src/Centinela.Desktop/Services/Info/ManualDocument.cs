using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Desktop.Services.Tray;

namespace Centinela.Desktop.Services.Info;

/// <summary>
/// The Spanish user manual: on Windows the same text as the WPF app's; on Linux and macOS their own tray (or menu
/// bar), starting with the system, volume and folders (<see cref="ManualSystemTexts"/>).
/// </summary>
static class ManualDocument
{
    public static IReadOnlyList<InfoBlock> Build(OsKind os)
    {
        var sys = ManualSystemTexts.For(os);
        List<InfoBlock> doc = [];
        doc.Add(new InfoHeading("Manual de Centinela"));
        doc.Add(new InfoParagraph("Centinela muestra, graba y captura el vídeo de tus cámaras IP (Tapo, Imou o cualquier cámara con RTSP) dentro de tu red local."));

        doc.Add(new InfoHeading("Añadir cámaras"));
        doc.Add(new InfoParagraph("Pulsa «➕ Añadir cámara», elige la marca y escribe la IP de la cámara."));
        doc.Add(new InfoParagraph("Tapo: usa la «Cuenta de cámara» que se crea en la app Tapo (Ajustes avanzados › Cuenta de cámara), no tu cuenta de TP-Link."));
        doc.Add(new InfoParagraph("Imou: el usuario es «admin» y la contraseña es el código de seguridad de la pegatina de la cámara. Si no hay vídeo, activa RTSP/ONVIF en la app Imou."));
        doc.Add(new InfoParagraph("Otra: escribe usuario y contraseña y, en «Avanzado», la URL RTSP de la cámara."));
        doc.Add(new InfoParagraph("«🔍 Buscar en red» encuentra con ONVIF las cámaras de tu red y rellena la IP por ti."));

        doc.Add(new InfoHeading("Vistas"));
        doc.Add(new InfoParagraph("Cuadrícula: Auto (se adapta al número de cámaras), 1, 4, 9 o 16 cámaras a la vez."));
        doc.Add(new InfoParagraph("Principal + miniaturas (derecha o izquierda): una cámara grande y las demás pequeñas a un lado; haz clic en una miniatura para cambiar la principal."));
        doc.Add(new InfoParagraph("Dos principales + miniaturas: dos cámaras grandes. Un clic en una miniatura la pone en la grande que lleva más tiempo sin cambiar; si la arrastras sobre una grande, va justo ahí. Centinela recuerda cuáles elegiste."));
        doc.Add(new InfoParagraph("Doble clic en una cámara la abre a pantalla completa. F11 pone toda la ventana a pantalla completa (Esc o F11 para salir)."));
        doc.Add(new InfoParagraph("Arrastra una cámara sobre otra para cambiar el orden."));

        doc.Add(new InfoHeading("Clic derecho"));
        doc.Add(new InfoParagraph("Con el botón derecho sobre una cámara: pantalla completa, captura, grabar o detener, editar, duplicar (crea una copia con «(copia)» en el nombre para cambiar solo lo necesario), restablecer el zoom y eliminar."));
        doc.Add(new InfoParagraph("En la ventana de añadir o editar, «Probar» comprueba durante unos segundos el vídeo principal y el secundario y te dice su resolución, códec y si hay audio, o qué falla."));

        doc.Add(new InfoHeading("Zoom"));
        doc.Add(new InfoParagraph("En las cámaras grandes y a pantalla completa, la rueda del ratón acerca la imagen (hasta 8×) hacia donde está el cursor. Con zoom, arrastra para moverte por la imagen."));
        doc.Add(new InfoParagraph("Pulsa 0 con el puntero sobre la cámara (o usa «Restablecer zoom» del clic derecho) para volver a verla entera. El zoom no se guarda."));

        doc.Add(new InfoHeading("Sonido"));
        doc.Add(new InfoParagraph("Si la cámara envía audio, las cámaras grandes y la pantalla completa muestran el botón 🔇. Púlsalo para oírla (🔊). Todas empiezan en silencio y solo suena una a la vez."));
        doc.Add(new InfoParagraph(sys.Sound));

        doc.Add(new InfoHeading("Grabar y capturas"));
        doc.Add(new InfoParagraph("El botón ⏺ de cada cámara empieza o detiene su grabación; «Grabar todas» graba todas las cámaras a la vez."));
        doc.Add(new InfoParagraph(sys.Folders));
        doc.Add(new InfoParagraph("Las grabaciones son archivos MKV que se reproducen con VLC."));

        doc.Add(new InfoHeading(sys.TrayHeading));
        doc.Add(new InfoParagraph(sys.Tray));
        doc.Add(new InfoParagraph($"Con «{TrayTexts.AutoStartLabel(os)}», Centinela se abre al iniciar sesión en el equipo."));

        doc.Add(new InfoHeading("Detección de movimiento"));
        doc.Add(new InfoParagraph("Cada cámara puede vigilar si hay movimiento. Viene apagada: actívala con el botón 👁 de la cámara, con «Detección de movimiento» del clic derecho o en Ver › Opciones de avisos…. Un 👁 tachado junto al nombre indica que está apagada."));
        doc.Add(new InfoParagraph("Mientras hay movimiento, la cámara lleva un marco rojo (en las miniaturas, en las grandes y a pantalla completa) que se quita 3 segundos después de que pare."));
        doc.Add(new InfoParagraph(sys.MotionNotice));
        doc.Add(new InfoParagraph("Cada cámara vigilada usa una conexión en calidad baja (la misma de la miniatura si se ve)."));

        doc.Add(new InfoHeading("Registro de movimiento"));
        doc.Add(new InfoParagraph("El botón «Movimiento» de la barra de estado muestra cuántos movimientos no has visto y abre el registro: hora de inicio, cámara, duración y cambio máximo. Desde ahí puedes copiar las líneas, abrir la carpeta o vaciarlo."));
        doc.Add(new InfoParagraph($"Cada movimiento se anota cuando termina (o al salir de Centinela). Los archivos (movimiento-AAAA-MM-DD.log, en {sys.Logs}) se borran a los 14 días."));

        doc.Add(new InfoHeading("Opciones de avisos"));
        doc.Add(new InfoParagraph("Ver › Opciones de avisos… tiene una tabla con cada cámara: detección, sensibilidad (Baja, Media o Alta), tiempo de espera entre avisos (30 s, 1 min, 5 min o 15 min), avisos de conexión y avisos de movimiento."));
        doc.Add(new InfoParagraph("También puedes activar sonidos de aviso al perder la conexión y con movimiento (solo cuando sale el aviso)."));
        doc.Add(new InfoParagraph("En las horas de silencio (por ejemplo, de 23:00 a 07:00, pueden pasar de medianoche) no hay avisos ni sonidos, pero el registro y el marco rojo siguen funcionando."));
        doc.Add(new InfoParagraph("Las opciones de cada cámara se guardan con las cámaras y van en la copia JSON (las copias antiguas se importan con la detección apagada). Los sonidos y las horas de silencio se guardan en los ajustes de Centinela y no van en la copia."));

        doc.Add(new InfoHeading("Copias de seguridad"));
        doc.Add(new InfoParagraph("Archivo › Exportar JSON guarda la lista de cámaras. Puedes exportarla sin contraseñas o con ellas, cifradas con una clave que eliges tú (al menos 8 caracteres)."));
        doc.Add(new InfoParagraph("Guarda la clave en un lugar seguro: sin ella no se pueden recuperar las contraseñas."));
        doc.Add(new InfoParagraph($"Cada vez que cambias la lista de cámaras se guarda una copia automática sin contraseñas (camaras-copia.json, en {sys.Documents} o en la carpeta que elijas en Archivo › Carpeta de copia automática)."));
        doc.Add(new InfoParagraph("Archivo › Importar JSON añade las cámaras nuevas y actualiza las que ya tenías (misma IP y puerto). Si la copia tiene contraseñas, se pide la clave."));

        doc.Add(new InfoHeading("Actualizaciones"));
        doc.Add(new InfoParagraph("Una vez al día, al arrancar, Centinela mira en GitHub si hay una versión nueva y te avisa. Solo envía el número de versión de Centinela."));
        doc.Add(new InfoParagraph("Nunca descarga nada por su cuenta: «Descargar» abre la página de GitHub. Puedes omitir una versión o desactivar el aviso con «Comprobar al arrancar» (Ayuda › Buscar actualizaciones…)."));
        doc.Add(new InfoParagraph("Solo cuentan las versiones publicadas en GitHub (no los borradores ni las versiones preliminares). Con CENTINELA_DATA_DIR (pruebas) no se comprueba al arrancar; Ayuda › Buscar actualizaciones… sigue funcionando."));

        doc.Add(new InfoHeading("Errores frecuentes"));
        foreach (var kind in Enum.GetValues<StreamErrorKind>())
        {
            var text = ErrorTranslator.Translate(kind, Brand.Custom, "La cámara", os);
            doc.Add(new InfoParagraph(text.Advice, text.Short + ": "));
        }

        doc.Add(new InfoHeading("Estadísticas"));
        doc.Add(new InfoParagraph("fps: imágenes por segundo que llegan de la cámara."));
        doc.Add(new InfoParagraph("ms: lo que tarda el equipo en preparar cada imagen desde que llega de la red hasta que está lista para mostrarse."));
        doc.Add(new InfoParagraph("GPU/CPU: si el vídeo se descodifica con la tarjeta gráfica (GPU, consume menos) o con el procesador (CPU)."));
        doc.Add(new InfoParagraph("conv.: lo que tarda el procesador en preparar cada imagen (reducirla al tamaño del recuadro y, si pinta el procesador, pasarla a color RGB)."));
        doc.Add(new InfoParagraph("pintado GPU/CPU: si la imagen se pinta con la tarjeta gráfica (OpenGL, consume menos) o con el procesador. Si la tarjeta gráfica no puede, Centinela pinta con el procesador sin avisar."));
        doc.Add(new InfoParagraph("El retraso real entre la cámara y la pantalla no se puede medir, porque el reloj de la cámara no está sincronizado con el del equipo."));
        return doc;
    }
}
