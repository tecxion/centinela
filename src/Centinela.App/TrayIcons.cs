using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Centinela.App;

/// <summary>Tray icons from the app icon (Assets/centinela.ico); while recording, a red dot is drawn over it.</summary>
static class TrayIcons
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(bool recording)
    {
        var size = Forms.SystemInformation.SmallIconSize;
        using var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/centinela.ico")).Stream;
        var icon = new Icon(stream, size);
        if (!recording) return icon;

        using (icon)
        using (var bitmap = new Bitmap(size.Width, size.Height))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawIcon(icon, new Rectangle(Point.Empty, size));
                var dot = size.Width * 7 / 16;
                using var red = new SolidBrush(Color.Red);
                using var ring = new Pen(Color.White, Math.Max(1, size.Width / 16f));
                var bounds = new Rectangle(size.Width - dot - 1, size.Height - dot - 1, dot, dot);
                g.FillEllipse(red, bounds);
                g.DrawEllipse(ring, bounds);
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
}
