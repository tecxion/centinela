namespace Centinela.Core.Platform;

/// <summary>A 32-bit image for the tray icon: straight (not premultiplied) BGRA, rows top to bottom.</summary>
public sealed record TrayImage(int Width, int Height, byte[] Bgra);

/// <summary>One entry of the tray menu; a null <see cref="Text"/> is a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? OnClick = null, bool Checkable = false, bool Checked = false);

/// <summary>
/// The icon in the system tray (Windows: notification area), its menu and its notices. Create, use and dispose on
/// the UI thread. Events are raised on the UI thread.
/// </summary>
public interface ITray : IDisposable
{
    void SetIcon(TrayImage image, string tooltip);
    /// <summary>Replaces the whole menu (it is rebuilt each time it opens from the latest items).</summary>
    void SetMenu(IReadOnlyList<TrayMenuItem> items);
    /// <summary>A system notice; a click on it runs <paramref name="onClick"/>, or raises <see cref="OpenRequested"/> without one.</summary>
    void ShowNotice(string title, string text, Action? onClick = null);
    /// <summary>Double click on the icon.</summary>
    event Action? OpenRequested;
    /// <summary>The menu is about to show (refresh state that may have changed outside the app).</summary>
    event Action? MenuOpening;
}
