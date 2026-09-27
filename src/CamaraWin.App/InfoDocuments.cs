using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CamaraWin.Core;

namespace CamaraWin.App;

/// <summary>The Spanish manual, licence and support documents shown by <see cref="InfoWindow"/>.</summary>
public static class InfoDocuments
{
    public const string WebsiteUri = "https://www.tecxart.es";
    public const string EmailUri = "mailto:tecxart@gmail.com";

    // Same text as the repository's LICENSE file.
    const string MitLicense = """
        MIT License

        Copyright (c) 2026 CamaraWin contributors

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    public static FlowDocument Manual()
    {
        var doc = NewDocument();
        doc.Blocks.Add(H("Manual de CamaraWin"));
        doc.Blocks.Add(P("CamaraWin muestra, graba y captura el vídeo de tus cámaras IP (Tapo, Imou o cualquier cámara con RTSP) dentro de tu red local."));

        doc.Blocks.Add(H("Añadir cámaras"));
        doc.Blocks.Add(P("Pulsa «➕ Añadir cámara», elige la marca y escribe la IP de la cámara."));
        doc.Blocks.Add(P("Tapo: usa la «Cuenta de cámara» que se crea en la app Tapo (Ajustes avanzados › Cuenta de cámara), no tu cuenta de TP-Link."));
        doc.Blocks.Add(P("Imou: el usuario es «admin» y la contraseña es el código de seguridad de la pegatina de la cámara. Si no hay vídeo, activa RTSP/ONVIF en la app Imou."));
        doc.Blocks.Add(P("Otra: escribe usuario y contraseña y, en «Avanzado», la URL RTSP de la cámara."));
        doc.Blocks.Add(P("«🔍 Buscar en red» encuentra con ONVIF las cámaras de tu red y rellena la IP por ti."));

        doc.Blocks.Add(H("Vistas"));
        doc.Blocks.Add(P("Cuadrícula: Auto (se adapta al número de cámaras), 1, 4, 9 o 16 cámaras a la vez."));
        doc.Blocks.Add(P("Principal + miniaturas: una cámara grande y las demás pequeñas; haz clic en una miniatura para cambiar la principal."));
        doc.Blocks.Add(P("Doble clic en una cámara la abre a pantalla completa. F11 pone toda la ventana a pantalla completa (Esc o F11 para salir)."));
        doc.Blocks.Add(P("Arrastra una cámara sobre otra para cambiar el orden."));

        doc.Blocks.Add(H("Grabar y capturas"));
        doc.Blocks.Add(P("El botón ⏺ de cada cámara empieza o detiene su grabación; «Grabar todas» graba todas las cámaras a la vez."));
        doc.Blocks.Add(P(@"Las grabaciones se guardan en Vídeos\CamaraWin y las capturas en Imágenes\CamaraWin."));
        doc.Blocks.Add(P("Las grabaciones son archivos MKV que se reproducen con VLC."));

        doc.Blocks.Add(H("Bandeja del sistema"));
        doc.Blocks.Add(P("La X de la ventana la oculta en la bandeja del sistema (junto al reloj) y las grabaciones continúan. «Salir» cierra CamaraWin del todo."));
        doc.Blocks.Add(P("Con «Arrancar con Windows», CamaraWin se abre al iniciar sesión en el equipo."));

        doc.Blocks.Add(H("Copias de seguridad"));
        doc.Blocks.Add(P("Archivo › Exportar JSON guarda la lista de cámaras. Puedes exportarla sin contraseñas o con ellas, cifradas con una clave que eliges tú (al menos 8 caracteres)."));
        doc.Blocks.Add(P("Guarda la clave en un lugar seguro: sin ella no se pueden recuperar las contraseñas."));
        doc.Blocks.Add(P(@"Cada vez que cambias la lista de cámaras se guarda una copia automática sin contraseñas (camaras-copia.json, en Documentos\CamaraWin o en la carpeta que elijas en Archivo › Carpeta de copia automática)."));
        doc.Blocks.Add(P("Archivo › Importar JSON añade las cámaras nuevas y actualiza las que ya tenías (misma IP y puerto). Si la copia tiene contraseñas, se pide la clave."));

        doc.Blocks.Add(H("Errores frecuentes"));
        foreach (var kind in Enum.GetValues<StreamErrorKind>())
        {
            var text = ErrorTranslator.Translate(kind, Brand.Custom, "La cámara");
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Bold(new Run(text.Short + ": ")));
            paragraph.Inlines.Add(new Run(text.Advice));
            doc.Blocks.Add(paragraph);
        }

        doc.Blocks.Add(H("Estadísticas"));
        doc.Blocks.Add(P("fps: imágenes por segundo que llegan de la cámara."));
        doc.Blocks.Add(P("ms: lo que tarda el equipo en preparar cada imagen desde que llega de la red hasta que está lista para mostrarse."));
        doc.Blocks.Add(P("GPU/CPU: si el vídeo se descodifica con la tarjeta gráfica (GPU, consume menos) o con el procesador (CPU)."));
        doc.Blocks.Add(P("El retraso real entre la cámara y la pantalla no se puede medir, porque el reloj de la cámara no está sincronizado con el del equipo."));
        return doc;
    }

    public static FlowDocument License()
    {
        var doc = NewDocument();
        doc.Blocks.Add(H("Licencia"));
        doc.Blocks.Add(P("Puedes usar, copiar, modificar, distribuir e incluso vender CamaraWin, siempre que mantengas el aviso de copyright y la licencia. Se ofrece «tal cual», sin garantías."));
        doc.Blocks.Add(new Paragraph(new Run(MitLicense)) { FontFamily = new FontFamily("Consolas"), FontSize = 12 });
        doc.Blocks.Add(H("FFmpeg"));
        doc.Blocks.Add(P("CamaraWin usa FFmpeg bajo licencia LGPL v2.1+ como bibliotecas separadas (carpeta ffmpeg); puedes sustituirlas por otra compilación compatible."));
        doc.Blocks.Add(Link("https://ffmpeg.org", "https://ffmpeg.org"));
        doc.Blocks.Add(Link("https://github.com/BtbN/FFmpeg-Builds", "https://github.com/BtbN/FFmpeg-Builds"));
        return doc;
    }

    public static FlowDocument Support()
    {
        var doc = NewDocument();
        doc.Blocks.Add(H("Soporte"));
        doc.Blocks.Add(P("¿Dudas, errores o sugerencias?"));
        doc.Blocks.Add(Link("www.tecxart.es", WebsiteUri));
        doc.Blocks.Add(Link("tecxart@gmail.com", EmailUri));
        doc.Blocks.Add(P("Incluye, si puedes, las líneas del Registro de errores (Registro › Copiar)."));
        return doc;
    }

    static FlowDocument NewDocument() => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 14,
        PagePadding = new Thickness(24),
    };

    static Paragraph H(string text) => new(new Bold(new Run(text))) { FontSize = 18, Margin = new Thickness(0, 16, 0, 6) };

    static Paragraph P(string text) => new(new Run(text));

    static Paragraph Link(string text, string uri) => new(new Hyperlink(new Run(text)) { NavigateUri = new Uri(uri) });
}
