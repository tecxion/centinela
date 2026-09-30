using Avalonia;
using Avalonia.Controls;
using Centinela.Desktop.Views;

namespace Centinela.Desktop.Services.Ui;

/// <summary>Opens web and mail links with the system's default handler; says so when it can't.</summary>
static class Links
{
    public static async Task OpenAsync(Visual from, string uri)
    {
        var top = TopLevel.GetTopLevel(from)!;
        bool opened;
        try { opened = await top.Launcher.LaunchUriAsync(new Uri(uri)); }
        catch (Exception) { opened = false; }
        if (!opened) await MessageDialog.ShowAsync(top as Window, $"No se pudo abrir {uri}");
    }
}
