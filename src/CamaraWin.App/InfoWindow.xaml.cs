using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace CamaraWin.App;

/// <summary>Read-only document window (manual, licence, support) whose links open in the default handler.</summary>
public partial class InfoWindow : Window
{
    public InfoWindow(string title, FlowDocument document)
    {
        InitializeComponent();
        Title = title;
        Viewer.Document = document;
        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OpenLink));
    }

    void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No browser or mail client registered.
            MessageBox.Show(this, $"No se pudo abrir {e.Uri.AbsoluteUri}", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        e.Handled = true;
    }
}
