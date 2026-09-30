using Centinela.Core;
using Centinela.Desktop.ViewModels;

namespace Centinela.Desktop.Tests;

/// <summary>«Añadir / Editar cámara»: validation, texts and how the form fills the edited copy (no network).</summary>
public sealed class CameraFormTests
{
    static CameraFields Valid(Brand brand = Brand.Tapo) =>
        new(brand, " Patio ", " 192.168.1.20 ", "554", " user ", "sec ret", "", "", UseUdp: false);

    static string ErrorOf(CameraFields fields)
    {
        var form = new CameraForm(new Camera());
        Assert.False(form.TryApply(fields, out var error, out _, out _));
        return error;
    }

    [Fact]
    public void Name_is_required() =>
        Assert.Equal("Pon un nombre a la cámara.", ErrorOf(Valid() with { Name = "  " }));

    [Theory]
    [InlineData(Brand.Tapo)]
    [InlineData(Brand.Imou)]
    public void Host_is_required_except_for_custom(Brand brand) =>
        Assert.Equal("La IP es obligatoria.", ErrorOf(Valid(brand) with { Host = " " }));

    [Fact]
    public void Custom_needs_a_main_url_but_no_host()
    {
        Assert.Equal("La URL principal es obligatoria.", ErrorOf(Valid(Brand.Custom) with { Host = "", MainUrl = "  " }));
        var form = new CameraForm(new Camera());
        Assert.True(form.TryApply(Valid(Brand.Custom) with { Host = "", MainUrl = "rtsp://cam/live" }, out _, out _, out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("")]
    public void Port_must_be_1_to_65535(string port) =>
        Assert.Equal("Puerto no válido.", ErrorOf(Valid() with { Port = port }));

    [Fact]
    public void Errors_come_in_the_dialog_order() =>
        Assert.Equal("Pon un nombre a la cámara.", ErrorOf(Valid() with { Name = "", Host = "", Port = "x" }));

    [Fact]
    public void Failed_apply_leaves_the_copy_untouched()
    {
        var form = new CameraForm(new Camera { Name = "Antes" });
        form.TryApply(Valid() with { Port = "0" }, out _, out _, out _);
        Assert.Equal("Antes", form.Result.Name);
    }

    [Fact]
    public void Apply_fills_every_field_of_a_copy()
    {
        var original = new Camera { Name = "Viejo", Brand = Brand.Tapo, Host = "10.0.0.1", MotionEnabled = true };
        var form = new CameraForm(original);

        var fields = new CameraFields(Brand.Imou, " Patio ", " 10.0.0.1 ", "8554", " admin ", " pw ",
            " rtsp://10.0.0.1:8554/main ", " rtsp://10.0.0.1:8554/sub ", UseUdp: true);
        Assert.True(form.TryApply(fields, out var error, out _, out _));

        var result = form.Result;
        Assert.Equal("", error);
        Assert.NotSame(original, result);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal("Viejo", original.Name);
        Assert.Equal(Brand.Imou, result.Brand);
        Assert.Equal("Patio", result.Name);
        Assert.Equal("10.0.0.1", result.Host);
        Assert.Equal(8554, result.Port);
        Assert.Equal("admin", result.User);
        Assert.Equal(" pw ", result.Password);   // passwords are kept exactly as typed
        Assert.True(result.UseUdp);
        Assert.True(result.MotionEnabled);       // what the form does not show is kept
    }

    [Fact]
    public void Empty_urls_mean_no_override()
    {
        var form = new CameraForm(new Camera { MainUrlOverride = "rtsp://a/1", SubUrlOverride = "rtsp://a/2" });
        Assert.True(form.TryApply(Valid() with { MainUrl = " ", SubUrl = "" }, out _, out _, out _));
        Assert.Null(form.Result.MainUrlOverride);
        Assert.Null(form.Result.SubUrlOverride);
    }

    [Fact]
    public void Changing_ip_or_port_moves_the_override_urls()
    {
        var camera = new Camera
        {
            Host = "192.168.1.20", Port = 554,
            MainUrlOverride = "rtsp://192.168.1.20:554/main", SubUrlOverride = "rtsp://192.168.1.20/sub",
        };
        var form = new CameraForm(camera);
        var fields = Valid() with { Host = "192.168.1.30", Port = "8554", MainUrl = camera.MainUrlOverride, SubUrl = camera.SubUrlOverride };

        Assert.True(form.TryApply(fields, out _, out var mainUrl, out var subUrl));

        Assert.Equal("rtsp://192.168.1.30:8554/main", mainUrl);
        Assert.Equal("rtsp://192.168.1.30:8554/sub", subUrl);
        Assert.Equal(mainUrl, form.Result.MainUrlOverride);
        Assert.Equal(subUrl, form.Result.SubUrlOverride);
        Assert.Equal("192.168.1.30", form.UrlHost);
        Assert.Equal(8554, form.UrlPort);

        // Applying again with the same address leaves the URLs as they are.
        Assert.True(form.TryApply(fields with { MainUrl = mainUrl, SubUrl = subUrl }, out _, out var again, out _));
        Assert.Equal(mainUrl, again);
    }

    [Fact]
    public void Same_address_keeps_the_urls_as_typed_but_trimmed()
    {
        var form = new CameraForm(new Camera { Host = "192.168.1.20", Port = 554 });
        Assert.True(form.TryApply(Valid() with { MainUrl = " rtsp://other/main " }, out _, out var mainUrl, out var subUrl));
        Assert.Equal("rtsp://other/main", mainUrl);
        Assert.Equal("", subUrl);
    }

    [Fact]
    public void Fields_of_shows_the_camera()
    {
        var fields = CameraForm.FieldsOf(new Camera
        {
            Brand = Brand.Imou, Name = "Patio", Host = "h", Port = 10554, User = "u", Password = "p",
            MainUrlOverride = "rtsp://h/1", UseUdp = true,
        });
        Assert.Equal(new CameraFields(Brand.Imou, "Patio", "h", "10554", "u", "p", "rtsp://h/1", "", true), fields);
    }

    [Fact]
    public void Hints_per_brand()
    {
        Assert.Equal("Usa el usuario y la contraseña de la «Cuenta de cámara» (app Tapo › Ajustes avanzados).",
            CameraForm.HintFor(Brand.Tapo));
        Assert.Equal("Usuario «admin». La contraseña es el código de seguridad de la pegatina. Si no conecta, activa RTSP/ONVIF en la app Imou.",
            CameraForm.HintFor(Brand.Imou));
        Assert.Equal("Pon la URL RTSP en «Avanzado» sin usuario ni contraseña (rtsp://IP:puerto/ruta). Escribe las credenciales en los campos Usuario y Contraseña: se guardan cifradas.",
            CameraForm.HintFor(Brand.Custom));
    }

    [Fact]
    public void Typing_a_password_replaces_the_unreadable_one()
    {
        var form = new CameraForm(new Camera { UnreadablePassword = "keychain:x" });
        Assert.True(form.TryApply(Valid() with { Password = "nueva" }, out _, out _, out _));
        Assert.Null(form.Result.UnreadablePassword);
    }

    [Fact]
    public void An_empty_password_keeps_the_unreadable_one()
    {
        var form = new CameraForm(new Camera { UnreadablePassword = "keychain:x" });
        Assert.True(form.TryApply(Valid() with { Password = "" }, out _, out _, out _));
        Assert.Equal("keychain:x", form.Result.UnreadablePassword);
    }

    [Fact]
    public void Password_note_text() =>
        Assert.Equal("La contraseña guardada no se puede leer en este equipo: escríbela de nuevo.", CameraForm.PasswordNote);
}
