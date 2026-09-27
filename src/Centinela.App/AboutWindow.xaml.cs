using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Centinela.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Centinela {AppInfo.Version}";
        RepoText.Text = "github.com/" + AppInfo.Repository;
        RepoLink.NavigateUri = new Uri(AppInfo.RepositoryUrl);
        SupportLink.NavigateUri = new Uri(InfoDocuments.WebsiteUri);
    }

    void Link_Click(object sender, RoutedEventArgs e)
    {
        var uri = ((Hyperlink)sender).NavigateUri.AbsoluteUri;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Win32Exception) { MessageBox.Show(this, $"No se pudo abrir {uri}", Title); }
    }
}
