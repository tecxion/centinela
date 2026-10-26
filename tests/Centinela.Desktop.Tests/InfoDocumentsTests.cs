using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Desktop.Services.Info;

namespace Centinela.Desktop.Tests;

public class InfoDocumentsTests
{
    [Fact]
    public void Version_is_always_major_minor_patch()
    {
        Assert.Equal(new Version(1, 4, 0), AppInfo.Normalize(new Version(1, 4)));
        Assert.Equal(new Version(1, 4, 2), AppInfo.Normalize(new Version(1, 4, 2, 7)));
    }

    [Fact]
    public void Manual_has_the_wpf_headings_in_order()
    {
        var blocks = ManualDocument.Build(OsKind.Windows);
        Assert.Equal(new InfoHeading("Manual de Centinela"), blocks[0]);
        var headings = blocks.OfType<InfoHeading>().Select(h => h.Text).Skip(1).ToArray();
        Assert.Equal(
            ["Añadir cámaras", "Vistas", "Clic derecho", "Zoom", "Sonido", "Grabar y capturas", "Bandeja del sistema",
             "Detección de movimiento", "Registro de movimiento", "Opciones de avisos", "Copias de seguridad",
             "Actualizaciones", "Errores frecuentes", "Estadísticas"],
            headings);
    }

    [Fact]
    public void Manual_notice_sounds_are_centinela_own_not_windows()
    {
        var blocks = ManualDocument.Build(OsKind.Windows);
        Assert.Contains(new InfoParagraph("También puedes activar sonidos de aviso al perder la conexión y con movimiento (solo cuando sale el aviso)."), blocks);
        Assert.DoesNotContain(blocks, b => b is InfoParagraph p && p.Text.Contains("sonidos de Windows"));
    }

    [Fact]
    public void Manual_explains_every_stream_error_after_its_heading()
    {
        var blocks = ManualDocument.Build(OsKind.Windows).ToList();
        var start = blocks.IndexOf(new InfoHeading("Errores frecuentes")) + 1;
        var kinds = Enum.GetValues<StreamErrorKind>();
        var expected = kinds.Select(kind =>
        {
            var text = ErrorTranslator.Translate(kind, Brand.Custom, "La cámara", OsKind.Windows);
            return (InfoBlock)new InfoParagraph(text.Advice, text.Short + ": ");
        });
        Assert.Equal(expected, blocks.Skip(start).Take(kinds.Length));
        Assert.Equal(new InfoHeading("Estadísticas"), blocks[start + kinds.Length]);
    }

    [Fact]
    public void Manual_on_macos_explains_the_local_network_permission()
    {
        var blocks = ManualDocument.Build(OsKind.MacOS).ToList();
        var start = blocks.IndexOf(new InfoHeading("Errores frecuentes"));
        Assert.Contains(blocks.Skip(start), b => b is InfoParagraph { Lead: "Sin conexión: " } p
            && p.Text.EndsWith("permite a Centinela el acceso a la red local en Ajustes del Sistema › Privacidad y seguridad › Red local."));
    }

    static string[] Paragraphs(OsKind os) =>
        ManualDocument.Build(os).OfType<InfoParagraph>().Select(p => p.Text).ToArray();

    [Fact]
    public void Manual_on_windows_keeps_its_system_texts()
    {
        var text = Paragraphs(OsKind.Windows);
        Assert.Contains("El sonido se para al ocultar Centinela en la bandeja o al cerrar la pantalla completa. El volumen se ajusta en el mezclador de volumen de Windows.", text);
        Assert.Contains(@"Las grabaciones se guardan en Vídeos\Centinela y las capturas en Imágenes\Centinela.", text);
        Assert.Contains("La X de la ventana la oculta en la bandeja del sistema (junto al reloj) y las grabaciones continúan. «Salir» cierra Centinela del todo.", text);
        Assert.Contains("Con «Arrancar con Windows», Centinela se abre al iniciar sesión en el equipo.", text);
        Assert.Contains("Sigue vigilando con Centinela oculta en la bandeja. Si la ventana está minimizada o en la bandeja, sale un aviso junto al reloj; haz clic en él para abrir la ventana. Como mucho sale un aviso por cámara cada tiempo de espera.", text);
        Assert.Contains(@"Cada movimiento se anota cuando termina (o al salir de Centinela). Los archivos (movimiento-AAAA-MM-DD.log, en %AppData%\Centinela\logs) se borran a los 14 días.", text);
        Assert.Contains(@"Cada vez que cambias la lista de cámaras se guarda una copia automática sin contraseñas (camaras-copia.json, en Documentos\Centinela o en la carpeta que elijas en Archivo › Carpeta de copia automática).", text);
    }

    [Fact]
    public void Manual_on_linux_has_its_own_tray_auto_start_volume_and_folders()
    {
        var text = Paragraphs(OsKind.Linux);
        Assert.Contains("La X de la ventana la oculta en la bandeja (el icono de la barra superior) y las grabaciones continúan. Si tu escritorio no tiene bandeja, la X minimiza la ventana. «Salir» cierra Centinela del todo.", text);
        Assert.Contains("Con «Arrancar al iniciar sesión», Centinela se abre al iniciar sesión en el equipo.", text);
        Assert.Contains(text, t => t.EndsWith("El volumen se ajusta en los ajustes de sonido del sistema."));
        Assert.Contains("Las grabaciones se guardan en Vídeos/Centinela y las capturas en Imágenes/Centinela.", text);
        Assert.Contains(text, t => t.Contains("en Documentos/Centinela o en la carpeta que elijas"));
        Assert.Contains(text, t => t.Contains("en ~/.local/share/centinela/logs)"));
    }

    [Fact]
    public void Manual_on_macos_uses_the_menu_bar_and_its_folders()
    {
        var text = Paragraphs(OsKind.MacOS);
        Assert.Contains(text, t => t.Contains("barra de menús"));
        Assert.Contains("Con «Abrir al iniciar sesión», Centinela se abre al iniciar sesión en el equipo.", text);
        Assert.Contains("Las grabaciones se guardan en Películas/Centinela y las capturas en Imágenes/Centinela.", text);
        Assert.Contains(text, t => t.Contains("en ~/Library/Application Support/Centinela/logs)"));
        Assert.DoesNotContain(text, t => t.Contains("bandeja"));
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Manual_elsewhere_never_says_windows_nor_uses_backslashes(OsKind os)
    {
        var blocks = ManualDocument.Build(os);
        Assert.DoesNotContain(blocks, b => b.ToString()!.Contains("Windows"));
        Assert.DoesNotContain(blocks, b => b.ToString()!.Contains('\\'));
        Assert.DoesNotContain(blocks, b => b.ToString()!.Contains("junto al reloj"));
        Assert.Equal(ManualDocument.Build(OsKind.Windows).Count, blocks.Count);
    }

    [Fact]
    public void License_has_the_mit_text_and_the_third_party_links()
    {
        var blocks = InfoDocuments.License();
        Assert.Contains(blocks, b => b is InfoCode c && c.Text.StartsWith("MIT License"));
        var links = blocks.OfType<InfoLink>().Select(l => l.Uri).ToArray();
        foreach (var uri in new[] { "https://ffmpeg.org", "https://github.com/BtbN/FFmpeg-Builds", "https://openal-soft.org",
                                    "https://github.com/dotnet/Silk.NET", "https://avaloniaui.net" })
            Assert.Contains(uri, links);
        Assert.DoesNotContain(blocks, b => b.ToString()!.Contains("NAudio"));
    }

    [Fact]
    public void Support_links_the_website_and_the_email()
    {
        var blocks = InfoDocuments.Support();
        Assert.Contains(new InfoLink("www.tecxart.es", AppInfo.WebsiteUri), blocks);
        Assert.Contains(new InfoLink("tecxart@gmail.com", AppInfo.EmailUri), blocks);
    }
}
