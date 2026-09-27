using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Centinela.Core;

namespace Centinela.App;

/// <summary>Ayuda › Buscar actualizaciones…: installed version, GitHub's latest release and its notes. Downloads nothing.</summary>
public partial class UpdateWindow : Window
{
    readonly UpdateChecker _checker;
    readonly AppSettings _settings;
    readonly Action _save;
    UpdateResult? _result;

    /// <param name="initial">A result already obtained (from the automatic check's notice); null checks now.</param>
    public UpdateWindow(UpdateChecker checker, AppSettings settings, Action save, UpdateResult? initial)
    {
        InitializeComponent();
        _checker = checker;
        _settings = settings;
        _save = save;
        InstalledText.Text = $"Versión instalada: {AppInfo.Version}";
        AutoCheckBox.IsChecked = settings.CheckUpdatesOnStartup;
        AutoCheckBox.Click += (_, _) =>
        {
            _settings.CheckUpdatesOnStartup = AutoCheckBox.IsChecked == true;
            _save();
        };
        DownloadButton.Click += (_, _) => Open(_result?.Url ?? _checker.ReleasesPage);
        SkipButton.Click += (_, _) =>
        {
            _settings.SkippedVersion = _result?.Latest?.ToString();
            _save();
            Close();
        };
        RetryButton.Click += async (_, _) => await CheckAsync();
        Loaded += async (_, _) =>
        {
            if (initial is null) await CheckAsync();
            else ShowResult(initial);
        };
    }

    async Task CheckAsync()
    {
        RetryButton.IsEnabled = false;
        StatusText.Text = "Comprobando…";
        NotesBox.Visibility = DownloadButton.Visibility = SkipButton.Visibility = Visibility.Collapsed;
        UpdateResult result;
        try { result = await _checker.CheckAsync(AppInfo.Version); }
        catch (Exception)
        {
            // CheckAsync translates HTTP failures itself; anything else is a response it could not read.
            result = new UpdateResult(UpdateStatus.Failed, Error: "Respuesta de GitHub no válida.");
        }
        if (!IsLoaded) return;
        if (UpdatePolicy.CountsAsChecked(result))
        {
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            _save();
        }
        ShowResult(result);
        RetryButton.IsEnabled = true;
    }

    void ShowResult(UpdateResult result)
    {
        _result = result;
        var available = result.Status == UpdateStatus.UpdateAvailable;
        StatusText.Text = result.Status switch
        {
            UpdateStatus.UpdateAvailable => $"Hay una versión nueva: {result.Latest}" +
                (result.PublishedAt is { } date ? $" ({date.LocalDateTime:d})" : ""),
            UpdateStatus.UpToDate => "Tienes la última versión.",
            UpdateStatus.NoReleases => "Aún no hay versiones publicadas.",
            _ => $"No se pudo comprobar: {result.Error}",
        };
        NotesBox.Text = result.Notes ?? "";
        NotesBox.Visibility = available && NotesBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Visibility = SkipButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Win32Exception) { MessageBox.Show(this, $"No se pudo abrir {url}", Title); }
    }
}
