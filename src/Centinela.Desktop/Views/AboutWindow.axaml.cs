using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Desktop.Services.Info;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>Version, licence and links; modal over the main window.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Centinela {AppInfo.Version}";
        RepoLink.Content = "github.com/" + AppInfo.Repository;
    }

    void RepoLink_Click(object? sender, RoutedEventArgs e) => _ = Links.OpenAsync(this, AppInfo.RepositoryUrl);

    void SupportLink_Click(object? sender, RoutedEventArgs e) => _ = Links.OpenAsync(this, AppInfo.WebsiteUri);

    // Avalonia's IsCancel/IsDefault only click the button; closing is ours.
    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
