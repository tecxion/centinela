using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Centinela.Core;
using Centinela.Desktop.Services.Info;
using Centinela.Desktop.Services.Notices;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>Update checks: Ayuda › Buscar actualizaciones… and a quiet daily check after startup.</summary>
public partial class MainWindow
{
    static readonly HttpClient Http = new();
    readonly UpdateChecker _updates = new(Http, AppInfo.Repository);
    UpdateWindow? _updateWindow;
    // An update found while the window was in the tray: its notice (with «Ver») shows when the window opens.
    UpdateResult? _pendingUpdate;

    void InitUpdates()
    {
        // Test runs never phone home on their own.
        if (AppPaths.IsDataDirectoryOverridden || !UpdatePolicy.ShouldAutoCheck(_vm.Settings.Settings, DateTimeOffset.Now)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            // An async void handler: an escaping exception would take the app down.
            try
            {
                // The user may have unticked the option in the first seconds.
                if (_closing || !_vm.Settings.Settings.CheckUpdatesOnStartup) return;
                await AutoCheckAsync();
            }
            catch (Exception)
            {
                // A quiet check: nothing to show; the next start tries again.
            }
        };
        timer.Start();
    }

    async Task AutoCheckAsync()
    {
        UpdateResult result;
        try { result = await _updates.CheckAsync(AppInfo.Version); }
        catch (Exception) { result = new UpdateResult(UpdateStatus.Failed, Error: "Respuesta de GitHub no válida."); }
        if (_closing) return;
        var settings = _vm.Settings.Settings;
        if (UpdatePolicy.CountsAsChecked(result))
        {
            settings.LastUpdateCheck = DateTimeOffset.Now;
            _vm.Settings.Save();
        }
        if (result.Status == UpdateStatus.Failed)
            _errorLog.Add(new ErrorLogEntry(DateTime.Now, "Centinela", "Actualizaciones", "No se pudo buscar actualizaciones", result.Error ?? ""));
        if (!UpdatePolicy.ShouldNotify(result, settings)) return;
        if (!IsHiddenInTray)
        {
            _notices.Show(UpdateToast(result));
            return;
        }
        // Started with the system / in the tray: the notice has no buttons, so clicking it opens the window and the
        // update details; opening the window any other way shows the usual notice instead.
        _pendingUpdate = result;
        ShowSystemNotice($"Centinela {result.Latest} disponible", "Haz clic aquí para ver la novedad.", () => UiThread.Post(() =>
        {
            if (_pendingUpdate is not { } pending) return;
            _pendingUpdate = null;
            ShowFromTray();
            if (!_closing && !_exitRequested) OpenUpdates(pending);
        }));
    }

    Toast UpdateToast(UpdateResult result) =>
        new($"Centinela {result.Latest} disponible", result.Title ?? "", ToastStyle.Info, "Ver", () => OpenUpdates(result));

    /// <summary>After the window leaves the tray: the update found meanwhile, once.</summary>
    void ShowPendingUpdate()
    {
        if (_pendingUpdate is not { } pending) return;
        _pendingUpdate = null;
        _notices.Show(UpdateToast(pending));
    }

    void Updates_Click(object? sender, RoutedEventArgs e) => OpenUpdates(null);

    void OpenUpdates(UpdateResult? initial)
    {
        if (_closing) return;
        if (_updateWindow is not null)
        {
            if (_updateWindow.WindowState == WindowState.Minimized) _updateWindow.WindowState = WindowState.Normal;
            _updateWindow.Activate();
            return;
        }
        var window = new UpdateWindow(_updates, _vm.Settings.Settings, _vm.Settings.Save, initial);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_updateWindow, window)) _updateWindow = null;
        };
        _updateWindow = window;
        window.Show(this);
    }
}
