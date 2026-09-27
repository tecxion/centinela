using System.Drawing;
using Forms = System.Windows.Forms;

namespace CamaraWin.App;

/// <summary>The notification-area icon and its menu. Create, use and dispose on the UI thread.</summary>
sealed class TrayController : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Icon _normal = TrayIcons.Create(recording: false);
    readonly Icon _recording = TrayIcons.Create(recording: true);
    readonly Forms.ToolStripMenuItem _recordAll;
    readonly Forms.ToolStripMenuItem _autoStart;

    public TrayController(bool autoStartEnabled)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => OpenRequested?.Invoke());
        _recordAll = new Forms.ToolStripMenuItem("Grabar todas", null, (_, _) => RecordAllRequested?.Invoke());
        menu.Items.Add(_recordAll);
        _autoStart = new Forms.ToolStripMenuItem("Arrancar con Windows") { CheckOnClick = true, Checked = autoStartEnabled };
        _autoStart.CheckedChanged += (_, _) => AutoStartToggled?.Invoke(_autoStart.Checked);
        menu.Items.Add(_autoStart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke());
        _icon = new Forms.NotifyIcon { Icon = _normal, Text = "CamaraWin", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action? RecordAllRequested;
    public event Action<bool>? AutoStartToggled;

    public void SetRecording(bool any)
    {
        _icon.Icon = any ? _recording : _normal;
        _recordAll.Text = any ? "Detener todas" : "Grabar todas";
    }

    public void SetAutoStart(bool enabled) => _autoStart.Checked = enabled;

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text.Length == 0 ? " " : text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _normal.Dispose();
        _recording.Dispose();
    }
}
