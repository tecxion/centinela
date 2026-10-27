using Centinela.Core;
using Centinela.Desktop.Services.Data;

namespace Centinela.Desktop.Tests;

public class FileSecretNoticeTests
{
    [Fact]
    public void Texts_are_exact()
    {
        Assert.Equal("Contraseñas", FileSecretNotice.Title);
        Assert.Equal("Las contraseñas se guardan cifradas en un archivo accesible solo por tu usuario.", FileSecretNotice.Text);
    }

    [Fact]
    public void Shown_once_when_the_passwords_use_the_file()
    {
        var settings = new AppSettings();
        Assert.True(FileSecretNotice.TryClaim(usesFileFallback: true, settings));
        Assert.True(settings.FileSecretNoticeShown);
        Assert.False(FileSecretNotice.TryClaim(usesFileFallback: true, settings));
    }

    [Fact]
    public void Never_while_the_passwords_are_in_the_keychain()
    {
        var settings = new AppSettings();
        Assert.False(FileSecretNotice.TryClaim(usesFileFallback: false, settings));
        Assert.False(settings.FileSecretNoticeShown);
    }
}
