using Centinela.Desktop.Services.Backup;

namespace Centinela.Desktop.Tests;

/// <summary>The passphrase checks shared by «Clave de la copia» and «Exportar cámaras».</summary>
public sealed class PassphraseRulesTests
{
    [Fact]
    public void Empty_passphrase_asks_for_one() => Assert.Equal("Escribe la clave.", PassphraseRules.Validate("", null));

    [Fact]
    public void Without_confirmation_there_is_no_minimum() => Assert.Null(PassphraseRules.Validate("corta", null));

    [Fact]
    public void Confirmed_passphrase_needs_the_minimum_length() =>
        Assert.Equal("La clave debe tener al menos 8 caracteres.", PassphraseRules.Validate("corta", "corta"));

    [Fact]
    public void Confirmation_must_match() =>
        Assert.Equal("Las claves no coinciden.", PassphraseRules.Validate("12345678", "12345679"));

    [Fact]
    public void Matching_long_passphrase_is_valid() => Assert.Null(PassphraseRules.Validate("12345678", "12345678"));
}
