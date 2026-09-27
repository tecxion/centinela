using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Centinela.Core;

namespace Centinela.App;

/// <summary>Update checks: Ayuda › Buscar actualizaciones… and a quiet daily check after startup.</summary>
public partial class MainWindow
{
    static readonly HttpClient Http = new();
    readonly UpdateChecker _updates = new(Http, AppInfo.Repository);
    UpdateWindow? _updateWindow;

    void InitUpdates()
    {
        // Test runs never phone home on their own.
        if (AppPaths.IsDataDirectoryOverridden || !UpdatePolicy.ShouldAutoCheck(_settings, DateTimeOffset.Now)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await AutoCheckAsync();
        };
        timer.Start();
    }

    async Task AutoCheckAsync()
    {
        UpdateResult result;
        // Runs from an async void timer handler: an escaping exception would take the app down.
        try { result = await _updates.CheckAsync(AppInfo.Version); }
        catch (Exception) { result = new UpdateResult(UpdateStatus.Failed, Error: "Respuesta de GitHub no válida."); }
        if (_closed) return;
        if (UpdatePolicy.CountsAsChecked(result))
        {
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            SaveSettingsQuietly();
        }
        if (result.Status == UpdateStatus.Failed)
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, "Centinela", "Actualizaciones", "No se pudo buscar actualizaciones", result.Error ?? ""));
        if (UpdatePolicy.ShouldNotify(result, _settings))
            ShowToast(new Toast($"Centinela {result.Latest} disponible", result.Title ?? "", ToastStyle.Info, "Ver", () => OpenUpdates(result)));
    }

    void Updates_Click(object sender, RoutedEventArgs e) => OpenUpdates(null);

    void OpenUpdates(UpdateResult? initial)
    {
        if (_updateWindow is { IsLoaded: true })
        {
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new UpdateWindow(_updates, _settings, SaveSettingsQuietly, initial) { Owner = this };
        _updateWindow.Show();
    }
}
