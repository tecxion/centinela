using CamaraWin.Core;
using CamaraWin.Core.Onvif;

namespace CamaraWin.Core.Tests;

public class BrandInferenceTests
{
    [Theory]
    [InlineData("tp-link", Brand.Tapo)]
    [InlineData("TP-LINK", Brand.Tapo)]
    [InlineData("Tapo", Brand.Tapo)]
    [InlineData("Dahua", Brand.Imou)]
    [InlineData("IMOU", Brand.Imou)]
    [InlineData("Hikvision", Brand.Custom)]
    [InlineData(null, Brand.Custom)]
    public void Infers_brand(string? manufacturer, Brand expected) =>
        Assert.Equal(expected, BrandInference.FromManufacturer(manufacturer));
}
