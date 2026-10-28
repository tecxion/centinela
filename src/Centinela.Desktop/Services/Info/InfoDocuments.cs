namespace Centinela.Desktop.Services.Info;

/// <summary>The Spanish licence and support documents shown by the info window.</summary>
static class InfoDocuments
{
    // Same text as the repository's LICENSE file.
    const string MitLicense = """
        MIT License

        Copyright (c) 2026 Centinela contributors

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

    public static IReadOnlyList<InfoBlock> License() =>
    [
        new InfoHeading("Licencia"),
        new InfoParagraph("Puedes usar, copiar, modificar, distribuir e incluso vender Centinela, siempre que mantengas el aviso de copyright y la licencia. Se ofrece «tal cual», sin garantías."),
        new InfoCode(MitLicense),
        new InfoHeading("FFmpeg"),
        new InfoParagraph("Centinela usa FFmpeg como bibliotecas separadas (carpeta ffmpeg), bajo licencia LGPL: v3 en Windows y Linux, v2.1 o posterior en macOS. Puedes sustituirlas por otra compilación compatible."),
        new InfoLink("https://ffmpeg.org", "https://ffmpeg.org"),
        new InfoLink("https://github.com/BtbN/FFmpeg-Builds", "https://github.com/BtbN/FFmpeg-Builds"),
        new InfoHeading("Sonido"),
        new InfoParagraph("El sonido se reproduce con OpenAL Soft, bajo licencia LGPL v2 (biblioteca separada), a través de Silk.NET (MIT)."),
        new InfoLink("https://openal-soft.org", "https://openal-soft.org"),
        new InfoLink("https://github.com/dotnet/Silk.NET", "https://github.com/dotnet/Silk.NET"),
        new InfoHeading("Interfaz"),
        new InfoParagraph("La interfaz usa Avalonia y CommunityToolkit.Mvvm, ambos bajo licencia MIT."),
        new InfoLink("https://avaloniaui.net", "https://avaloniaui.net"),
    ];

    public static IReadOnlyList<InfoBlock> Support() =>
    [
        new InfoHeading("Soporte"),
        new InfoParagraph("¿Dudas, errores o sugerencias?"),
        new InfoLink("www.tecxart.es", AppInfo.WebsiteUri),
        new InfoLink("tecxart@gmail.com", AppInfo.EmailUri),
        new InfoParagraph("Incluye, si puedes, las líneas del Registro de errores (Registro › Copiar)."),
    ];
}
