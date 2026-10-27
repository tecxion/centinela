using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Centinela.Core.Platform;

namespace Centinela.Desktop.Services.Tray;

/// <summary>
/// The tray icon on Linux and macOS, with Avalonia's <see cref="TrayIcon"/> (on Linux a StatusNotifierItem, which
/// needs a StatusNotifierWatcher: check <see cref="ITrayAvailability"/> first). A single click on the icon raises
/// <see cref="OpenRequested"/> (there is no reliable double click there). Notices go through the system's
/// notifications (<see cref="INotifier"/>); without one they do not show.
/// </summary>
sealed class AvaloniaTray : ITray
{
    readonly INotifier? _notifier;
    readonly TrayIcon _icon;
    readonly NativeMenu _menu = new();
    // The entries shown now: menu items run the current entry's action, so an update in place keeps them right.
    IReadOnlyList<TrayMenuItem> _entries = [];
    // Each image is converted once (the window passes the same two again and again).
    readonly Dictionary<TrayImage, (Bitmap Bitmap, WindowIcon Icon)> _icons = new(ReferenceEqualityComparer.Instance);
    bool _disposed;

    public event Action? OpenRequested;
    public event Action? MenuOpening;

    /// <summary>On the UI thread, with the Avalonia application running.</summary>
    public AvaloniaTray(INotifier? notifier)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Avalonia no está en marcha.");
        _notifier = notifier;
        _menu.Opening += (_, _) => OnUiThread(() => MenuOpening?.Invoke());
        // Hidden until it has its icon.
        _icon = new TrayIcon { Menu = _menu, IsVisible = false };
        _icon.Clicked += (_, _) => OnUiThread(RaiseOpenRequested);
        // Registered with the application, which removes it if the app ends without disposing it.
        var icons = TrayIcon.GetIcons(app);
        if (icons is null)
        {
            icons = [];
            TrayIcon.SetIcons(app, icons);
        }
        icons.Add(_icon);
    }

    public void SetIcon(TrayImage image, string tooltip)
    {
        if (_disposed) return;
        if (!_icons.TryGetValue(image, out var icon))
        {
            icon = ToIcon(image);
            _icons[image] = icon;
        }
        _icon.Icon = icon.Icon;
        _icon.ToolTipText = tooltip;
        _icon.IsVisible = true;
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        if (_disposed) return;
        var entries = items.ToList();
        if (SameShape(entries))
        {
            // Same items with new texts or checks: updated in place, so a menu being shown is not torn down.
            for (var i = 0; i < entries.Count; i++)
            {
                if (_menu.Items[i] is not NativeMenuItem item) continue;
                item.Header = entries[i].Text;
                item.IsChecked = entries[i].Checked;
            }
            _entries = entries;
            return;
        }
        _entries = entries;
        _menu.Items.Clear();
        for (var i = 0; i < entries.Count; i++) _menu.Items.Add(NewItem(entries[i], i));
    }

    public void ShowNotice(string title, string text, Action? onClick = null)
    {
        if (_disposed) return;
        // As the Windows tray: a click on a notice without its own action opens the window.
        _notifier?.Show(title, text, onClick ?? RaiseOpenRequested);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.IsVisible = false;
        try
        {
            if (Application.Current is { } app) TrayIcon.GetIcons(app)?.Remove(_icon);
            _icon.Dispose();
        }
        catch (Exception)
        {
            // Ending anyway; the icon goes with the process.
        }
        foreach (var (bitmap, _) in _icons.Values) bitmap.Dispose();
        _icons.Clear();
    }

    bool SameShape(List<TrayMenuItem> entries) =>
        entries.Count == _entries.Count && entries.Count == _menu.Items.Count
        && entries.Select(Shape).SequenceEqual(_entries.Select(Shape));

    static (bool Separator, bool Checkable) Shape(TrayMenuItem entry) => (entry.Text is null, entry.Checkable);

    NativeMenuItemBase NewItem(TrayMenuItem entry, int index)
    {
        if (entry.Text is null) return new NativeMenuItemSeparator();
        var item = new NativeMenuItem(entry.Text);
        if (entry.Checkable)
        {
            item.ToggleType = NativeMenuItemToggleType.CheckBox;
            item.IsChecked = entry.Checked;
        }
        item.Click += (_, _) => OnUiThread(() =>
        {
            if (!_disposed && index < _entries.Count) _entries[index].OnClick?.Invoke();
        });
        return item;
    }

    void RaiseOpenRequested()
    {
        if (!_disposed) OpenRequested?.Invoke();
    }

    /// <summary>ITray raises its events on the UI thread, whatever thread the platform's menu or icon uses.</summary>
    static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Straight BGRA to an Avalonia icon (the bitmap is kept: some platforms read it later).</summary>
    static unsafe (Bitmap, WindowIcon) ToIcon(TrayImage image)
    {
        Bitmap bitmap;
        fixed (byte* pixels = image.Bgra)
        {
            bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, (nint)pixels,
                new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Width * 4);
        }
        try
        {
            return (bitmap, new WindowIcon(bitmap));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
