namespace CamaraWin.Core.Onvif;

public static class BrandInference
{
    public static Brand FromManufacturer(string? manufacturer)
    {
        var m = manufacturer ?? "";
        if (ContainsAny(m, "tp-link", "tplink", "tapo")) return Brand.Tapo;
        if (ContainsAny(m, "dahua", "imou")) return Brand.Imou;
        return Brand.Custom;
    }

    static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
}
