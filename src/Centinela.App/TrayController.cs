using System.Drawing;
using Forms = System.Windows.Forms;

namespace Centinela.App;

/// <summary>The notification-area icon and its menu. Create, use and dispose on the UI thread.</summary>
sealed class TrayController : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Icon _normal = TrayIcons.Create(recording: false);
    readonly Icon _recording = TrayIcons.Create(recording: true);
    readonly Forms.ToolStripMenuItem _recordAll;
    readonly Forms.ToolStripMenuItem _autoStart;
    // Set while the check mark is changed from code, so only user clicks raise AutoStartToggled.
    bool _settingAutoStart;
    // What a click on the latest balloon does (each ShowBalloon replaces it); null opens the window.
    Action? _balloonClick;

    /// <param name="autoStartAvailable">False hides «Arrancar con Windows» (test runs must not touch the Run key).</param>
    public TrayController(bool autoStartEnabled, bool autoStartAvailable = true)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => OpenRequested?.Invoke());
        _recordAll = new Forms.ToolStripMenuItem("Grabar todas", null, (_, _) => RecordAllRequested?.Invoke());
        menu.Items.Add(_recordAll);
        _autoStart = new Forms.ToolStripMenuItem("Arrancar con Windows")
        {
            CheckOnClick = true, Checked = autoStartEnabled, Visible = autoStartAvailable,
        };
        _autoStart.CheckedChanged += (_, _) =>
        {
            if (!_settingAutoStart) AutoStartToggled?.Invoke(_autoStart.Checked);
        };
        menu.Items.Add(_autoStart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke());
        menu.Opening += (_, _) => MenuOpening?.Invoke();
        _icon = new Forms.NotifyIcon { Icon = _normal, Text = "Centinela", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            if (click is not null) click();
            else OpenRequested?.Invoke();
        };
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? RecordAllRequested;
    public event Action<bool>? AutoStartToggled;
    /// <summary>The context menu is about to show (refresh state that may have changed outside the app).</summary>
    public event Action? MenuOpening;

    public void SetRecording(bool any)
    {
        _icon.Icon = any ? _recording : _normal;
        _recordAll.Text = any ? "Detener todas" : "Grabar todas";
    }

    /// <summary>Updates the check mark without raising <see cref="AutoStartToggled"/>.</summary>
    public void SetAutoStart(bool enabled)
    {
        _settingAutoStart = true;
        try { _autoStart.Checked = enabled; }
        finally { _settingAutoStart = false; }
    }

    /// <summary>A click on the balloon runs <paramref name="onClick"/>, or raises <see cref="OpenRequested"/> without one.</summary>
    public void ShowBalloon(string title, string text, Action? onClick = null)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(5000, title, text.Length == 0 ? " " : text, Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _normal.Dispose();
        _recording.Dispose();
    }
}
