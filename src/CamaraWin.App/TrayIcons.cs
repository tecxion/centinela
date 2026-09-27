using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CamaraWin.App;

/// <summary>Tray icons drawn at runtime, so the project needs no .ico file.</summary>
static class TrayIcons
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(bool recording)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(0x33, 0x99, 0xFF));
            g.FillRectangle(body, 2, 9, 20, 15);
            g.FillPolygon(body, [new Point(22, 13), new Point(30, 9), new Point(30, 24), new Point(22, 20)]);
            using var lens = new SolidBrush(Color.White);
            g.FillEllipse(lens, 7, 12, 9, 9);
            if (recording)
            {
                using var dot = new SolidBrush(Color.Red);
                g.FillEllipse(dot, 18, 18, 13, 13);
            }
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
