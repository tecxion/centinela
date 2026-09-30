using Centinela.Core;
using Centinela.Desktop.Services.Errors;

namespace Centinela.Desktop.Tests;

public class ErrorTextTests
{
    [Fact]
    public void Auth_failure_without_a_stored_password_says_it_is_missing()
    {
        var camera = new Camera { Name = "Garaje", Password = "" };
        var text = ErrorText.Translate(camera, StreamErrorKind.AuthFailed);
        Assert.Equal(("Falta contraseña", "Garaje: falta la contraseña"), (text.Short, text.Title));
    }

    [Fact]
    public void Other_errors_use_the_core_translation()
    {
        var camera = new Camera { Name = "Garaje", Password = "x", Brand = Brand.Tapo };
        Assert.Equal(ErrorTranslator.Translate(StreamErrorKind.Unreachable, Brand.Tapo, "Garaje"),
            ErrorText.Translate(camera, StreamErrorKind.Unreachable));
    }
}
