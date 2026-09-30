using Centinela.Core;
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
        var blocks = ManualDocument.Build();
        Assert.Equal(new InfoHeading("Manual de Centinela"), blocks[0]);
        var headings = blocks.OfType<InfoHeading>().Select(h => h.Text).Skip(1).ToArray();
        Assert.Equal(
            ["Añadir cámaras", "Vistas", "Clic derecho", "Zoom", "Sonido", "Grabar y capturas", "Bandeja del sistema",
             "Detección de movimiento", "Registro de movimiento", "Opciones de avisos", "Copias de seguridad",
             "Actualizaciones", "Errores frecuentes", "Estadísticas"],
            headings);
    }

    [Fact]
    public void Manual_explains_every_stream_error_after_its_heading()
    {
        var blocks = ManualDocument.Build().ToList();
        var start = blocks.IndexOf(new InfoHeading("Errores frecuentes")) + 1;
        var kinds = Enum.GetValues<StreamErrorKind>();
        var expected = kinds.Select(kind =>
        {
            var text = ErrorTranslator.Translate(kind, Brand.Custom, "La cámara");
            return (InfoBlock)new InfoParagraph(text.Advice, text.Short + ": ");
        });
        Assert.Equal(expected, blocks.Skip(start).Take(kinds.Length));
        Assert.Equal(new InfoHeading("Estadísticas"), blocks[start + kinds.Length]);
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
