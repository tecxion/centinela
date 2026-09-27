using Centinela.Core;

namespace Centinela.Core.Tests;

public class ErrorTranslatorTests
{
    public static TheoryData<StreamErrorKind, Brand> All()
    {
        var data = new TheoryData<StreamErrorKind, Brand>();
        foreach (var k in Enum.GetValues<StreamErrorKind>())
            foreach (var b in Enum.GetValues<Brand>()) data.Add(k, b);
        return data;
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_kind_and_brand_has_spanish_text(StreamErrorKind kind, Brand brand)
    {
        var t = ErrorTranslator.Translate(kind, brand, "Garaje");
        Assert.False(string.IsNullOrWhiteSpace(t.Short));
        Assert.True(t.Short.Length <= 30, t.Short);
        Assert.StartsWith("Garaje", t.Title);
        Assert.False(string.IsNullOrWhiteSpace(t.Advice));
    }

    [Fact]
    public void Tapo_auth_advice_mentions_camera_account() =>
        Assert.Contains("Cuenta de cámara", ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Tapo, "X").Advice);

    [Fact]
    public void Imou_auth_advice_mentions_safety_code() =>
        Assert.Contains("código de seguridad", ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Imou, "X").Advice);

    [Fact]
    public void Imou_not_found_advice_mentions_rtsp() =>
        Assert.Contains("RTSP", ErrorTranslator.Translate(StreamErrorKind.NotFound, Brand.Imou, "X").Advice);

    [Fact]
    public void Short_texts_match_spec()
    {
        Assert.Equal("Contraseña incorrecta", ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Custom, "X").Short);
        Assert.Equal("Sin conexión", ErrorTranslator.Translate(StreamErrorKind.Unreachable, Brand.Custom, "X").Short);
        Assert.Equal("Vídeo no disponible", ErrorTranslator.Translate(StreamErrorKind.NotFound, Brand.Custom, "X").Short);
        Assert.Equal("Cámara ocupada", ErrorTranslator.Translate(StreamErrorKind.CameraBusy, Brand.Custom, "X").Short);
        Assert.Equal("Error de la cámara", ErrorTranslator.Translate(StreamErrorKind.ServerError, Brand.Custom, "X").Short);
        Assert.Equal("Imagen congelada", ErrorTranslator.Translate(StreamErrorKind.Stalled, Brand.Custom, "X").Short);
        Assert.Equal("Error de conexión", ErrorTranslator.Translate(StreamErrorKind.Unknown, Brand.Custom, "X").Short);
    }
}
