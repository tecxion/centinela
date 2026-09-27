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
    // An update found while the window was in the tray: its toast (with «Ver») shows when the window opens.
    UpdateResult? _pendingUpdate;

    void InitUpdates()
    {
        // Test runs never phone home on their own.
        if (AppPaths.IsDataDirectoryOverridden || !UpdatePolicy.ShouldAutoCheck(_settings, DateTimeOffset.Now)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            // The user may have unticked the option in the first seconds.
            if (!_settings.CheckUpdatesOnStartup) return;
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
        if (!UpdatePolicy.ShouldNotify(result, _settings)) return;
        if (!IsHiddenInTray)
        {
            ShowToast(UpdateToast(result));
            return;
        }
        // Autostart/tray: the balloon has no buttons, so clicking it opens the window and the update details;
        // opening the window any other way shows the usual toast instead.
        _pendingUpdate = result;
        _tray.ShowBalloon($"Centinela {result.Latest} disponible", "Haz clic aquí para ver la novedad.", () => Dispatcher.BeginInvoke(() =>
        {
            if (_pendingUpdate is not { } pending) return;
            _pendingUpdate = null;
            ShowFromTray();
            if (!_closed && !_exitRequested) OpenUpdates(pending);
        }));
    }

    Toast UpdateToast(UpdateResult result) =>
        new($"Centinela {result.Latest} disponible", result.Title ?? "", ToastStyle.Info, "Ver", () => OpenUpdates(result));

    /// <summary>After the window leaves the tray: the update found meanwhile, once.</summary>
    void ShowPendingUpdate()
    {
        if (_pendingUpdate is not { } pending) return;
        _pendingUpdate = null;
        ShowToast(UpdateToast(pending));
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
