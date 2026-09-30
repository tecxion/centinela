using Centinela.Core;

namespace Centinela.Desktop.Services.Layout;

/// <summary>The «Vista» choices: five grid sizes, then the layouts, in this order.</summary>
static class LayoutChoices
{
    static readonly GridMode[] GridModes = [GridMode.Auto, GridMode.One, GridMode.Four, GridMode.Nine, GridMode.Sixteen];
    static readonly LayoutMode[] Layouts = [LayoutMode.Featured, LayoutMode.FeaturedLeft, LayoutMode.Dual];

    public static IReadOnlyList<string> Labels { get; } =
    [
        "Auto", "1", "4", "9", "16",
        "Principal + miniaturas (derecha)", "Principal + miniaturas (izquierda)", "Dos principales + miniaturas",
    ];

    public static int IndexFor(AppSettings settings) =>
        settings.LayoutMode == LayoutMode.Grid
            ? Math.Max(0, Array.IndexOf(GridModes, settings.GridMode))
            : GridModes.Length + Math.Max(0, Array.IndexOf(Layouts, settings.LayoutMode));

    public static void Apply(AppSettings settings, int index)
    {
        if (index >= GridModes.Length)
        {
            settings.LayoutMode = Layouts[Math.Clamp(index - GridModes.Length, 0, Layouts.Length - 1)];
            return;
        }
        settings.LayoutMode = LayoutMode.Grid;
        settings.GridMode = GridModes[Math.Max(0, index)];
    }
}
