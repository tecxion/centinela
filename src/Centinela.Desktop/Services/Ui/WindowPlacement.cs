namespace Centinela.Desktop.Services.Ui;

/// <summary>
/// Saved window placement: whether a corner is still on a connected screen (a monitor may have been
/// unplugged), and the DIP/pixel conversion that keeps settings.json compatible with the WPF app,
/// which stores Left/Top as pixels divided by the primary-monitor scale.
/// </summary>
static class WindowPlacement
{
    const int Margin = 100;

    public static bool IsVisibleOn(IEnumerable<(int X, int Y, int Width, int Height)> screens, double left, double top) =>
        screens.Any(s => left >= s.X && top >= s.Y && left < s.X + s.Width - Margin && top < s.Y + s.Height - Margin);

    public static int ToPixels(double dip, double scale) => (int)Math.Round(dip * scale);

    public static double ToDips(int px, double scale) => px / scale;
}
