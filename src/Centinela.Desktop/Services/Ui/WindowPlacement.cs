namespace Centinela.Desktop.Services.Ui;

/// <summary>Whether a saved window corner is still on a connected screen (a monitor may have been unplugged).</summary>
static class WindowPlacement
{
    const int Margin = 100;

    public static bool IsVisibleOn(IEnumerable<(int X, int Y, int Width, int Height)> screens, double left, double top) =>
        screens.Any(s => left >= s.X && top >= s.Y && left < s.X + s.Width - Margin && top < s.Y + s.Height - Margin);
}
