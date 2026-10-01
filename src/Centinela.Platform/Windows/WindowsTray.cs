using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Centinela.Core.Platform;
using static Centinela.Platform.Windows.TrayInterop;

namespace Centinela.Platform.Windows;

/// <summary>
/// The notification-area icon through Shell_NotifyIcon (version 4), as Centinela 1.3 did with WinForms: a menu on
/// right click, the window on double click and Windows balloons whose click runs the notice's action. Its hidden
/// window lives on the UI thread, whose message loop (Avalonia's) serves it.
/// </summary>
/// <remarks>
/// The window is a hidden top-level one rather than a message-only (HWND_MESSAGE) window: message-only windows do
/// not get broadcasts, and the «TaskbarCreated» broadcast is how the icon comes back after Explorer restarts.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsTray : ITray
{
    const uint IconId = 1;
    const uint CallbackMessage = WM_APP + 1;

    // Kept in a field: the native window calls it for as long as it exists, so the GC must not collect it.
    readonly WndProc _wndProc;
    readonly string _className = "Centinela.Tray." + Guid.NewGuid().ToString("N");
    readonly IntPtr _instance = GetModuleHandleW(null);
    readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    // Where errors from the handlers go: thrown through the native WndProc they would end the process.
    readonly SynchronizationContext? _context = SynchronizationContext.Current;
    IntPtr _hwnd;
    IntPtr _icon;
    string _tooltip = "";
    bool _added;
    bool _disposed;
    IReadOnlyList<TrayMenuItem> _menu = [];
    // What a click on the latest notice does (each ShowNotice replaces it); null raises OpenRequested.
    Action? _noticeClick;

    public WindowsTray()
    {
        _wndProc = OnMessage;
        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wndClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, _className, "Centinela", WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            UnregisterClassW(_className, _instance);
            throw new Win32Exception(error);
        }
        // An elevated Centinela still hears Explorer (medium integrity) announce a new taskbar.
        if (_taskbarCreated != 0) ChangeWindowMessageFilterEx(_hwnd, _taskbarCreated, MSGFLT_ALLOW, IntPtr.Zero);
    }

    public event Action? OpenRequested;
    public event Action? MenuOpening;

    public void SetIcon(TrayImage image, string tooltip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var icon = CreateIcon(image);
        var previous = _icon;
        _icon = icon;
        _tooltip = TrayText.Fit(tooltip, TrayText.TipMax);
        if (_added) Notify(NIM_MODIFY, NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        else AddIcon();
        if (previous != IntPtr.Zero) DestroyIcon(previous);
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _menu = items.ToArray();
    }

    public void ShowNotice(string title, string text, Action? onClick = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _noticeClick = onClick;
        if (!_added) return;   // No icon yet: Windows has nothing to hang the balloon on.
        var data = NewData(NIF_INFO);
        data.szInfoTitle = TrayText.Fit(title, TrayText.TitleMax);
        data.szInfo = TrayText.Fit(text, TrayText.InfoMax);
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    void AddIcon()
    {
        _added = Notify(NIM_ADD, NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!_added) return;
        var version = NewData(0);
        version.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref version);
    }

    bool Notify(uint message, uint flags)
    {
        var data = NewData(flags);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = _tooltip;
        return Shell_NotifyIconW(message, ref data);
    }

    NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == CallbackMessage)
            {
                OnTrayEvent((uint)LowWord(lParam), wParam);
                return IntPtr.Zero;
            }
            if (msg == _taskbarCreated && _taskbarCreated != 0)
            {
                // Explorer restarted: the old icon is gone.
                _added = false;
                if (_icon != IntPtr.Zero) AddIcon();
                return IntPtr.Zero;
            }
        }
        catch (Exception e) when (_context is not null)
        {
            var captured = ExceptionDispatchInfo.Capture(e);
            _context.Post(_ => captured.Throw(), null);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    void OnTrayEvent(uint trayEvent, IntPtr anchor)
    {
        switch (trayEvent)
        {
            case WM_CONTEXTMENU:
                ShowMenu(UnpackPoint(anchor));
                break;
            case WM_LBUTTONDBLCLK:
                OpenRequested?.Invoke();
                break;
            case NIN_BALLOONUSERCLICK:
                var click = _noticeClick;
                _noticeClick = null;
                if (click is not null) click();
                else OpenRequested?.Invoke();
                break;
        }
    }

    void ShowMenu(POINT at)
    {
        MenuOpening?.Invoke();
        if (_disposed) return;
        var items = _menu;
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        int chosen;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text is null)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                var flags = MF_STRING | (item.Checkable && item.Checked ? MF_CHECKED : 0);
                // Command ids start at 1: TrackPopupMenuEx returns 0 when the menu is dismissed.
                AppendMenuW(menu, flags, (UIntPtr)(uint)(i + 1), item.Text);
            }
            // Without the foreground the menu would not close on a click elsewhere.
            SetForegroundWindow(_hwnd);
            chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, at.X, at.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
        if (!_disposed && chosen > 0 && chosen <= items.Count) items[chosen - 1].OnClick?.Invoke();
    }

    static IntPtr CreateIcon(TrayImage image)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Bgra.Length != image.Width * image.Height * 4)
            throw new ArgumentException("La imagen del icono no es BGRA de 32 bits del tamaño indicado.", nameof(image));
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = image.Width,
            biHeight = -image.Height,   // Negative: rows top to bottom.
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        var color = CreateDIBSection(IntPtr.Zero, ref header, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        // A monochrome mask (rows padded to 16 bits), all zero: the alpha channel of the color bitmap decides.
        var mask = CreateBitmap(image.Width, image.Height, 1, 1, new byte[(image.Width + 15) / 16 * 2 * image.Height]);
        try
        {
            if (mask == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(image.Bgra, 0, bits, image.Bgra.Length);
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            var icon = CreateIconIndirect(ref info);
            if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return icon;
        }
        finally
        {
            // CreateIconIndirect copies both bitmaps.
            DeleteObject(color);
            if (mask != IntPtr.Zero) DeleteObject(mask);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_added)
            {
                var data = NewData(0);
                Shell_NotifyIconW(NIM_DELETE, ref data);
            }
        }
        catch (Exception)
        {
            // Dispose never throws.
        }
        _added = false;
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        UnregisterClassW(_className, _instance);
        _menu = [];
        _noticeClick = null;
        OpenRequested = null;
        MenuOpening = null;
    }
}
