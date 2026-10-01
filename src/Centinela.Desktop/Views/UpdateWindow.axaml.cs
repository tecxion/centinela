using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Centinela.Core;
using Centinela.Core.Platform;
using Centinela.Desktop.Services.Info;
using Centinela.Desktop.Services.Ui;

namespace Centinela.Desktop.Views;

/// <summary>Ayuda › Buscar actualizaciones…: installed version, GitHub's latest release and its notes. Downloads nothing.</summary>
public partial class UpdateWindow : Window
{
    readonly UpdateChecker? _checker;
    readonly AppSettings? _settings;
    readonly Action? _save;
    UpdateResult? _result;
    bool _closed;

    public UpdateWindow() => InitializeComponent();

    /// <param name="initial">A result already obtained (from the automatic check's notice); null checks now.</param>
    public UpdateWindow(UpdateChecker checker, AppSettings settings, Action save, UpdateResult? initial) : this()
    {
        _checker = checker;
        _settings = settings;
        _save = save;
        InstalledText.Text = $"Versión instalada: {AppInfo.Version}";
        AutoCheckBox.IsChecked = settings.CheckUpdatesOnStartup;
        Closed += (_, _) => _closed = true;
        Opened += async (_, _) =>
        {
            if (initial is null) await CheckAsync();
            else ShowResult(initial);
        };
    }

    async Task CheckAsync()
    {
        if (_checker is null || _settings is null) return;
        RetryButton.IsEnabled = false;
        StatusText.Text = "Comprobando…";
        NotesBox.IsVisible = AssetText.IsVisible = DownloadButton.IsVisible = SkipButton.IsVisible = false;
        UpdateResult result;
        try { result = await _checker.CheckAsync(AppInfo.Version); }
        catch (Exception)
        {
            // CheckAsync translates HTTP failures itself; anything else is a response it could not read.
            result = new UpdateResult(UpdateStatus.Failed, Error: "Respuesta de GitHub no válida.");
        }
        if (_closed) return;
        if (UpdatePolicy.CountsAsChecked(result))
        {
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            _save?.Invoke();
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
        var asset = available && result.Latest is { } latest ? AssetFor(latest) : null;
        AssetText.Text = asset is null ? "" : $"Archivo para este equipo: {asset}";
        AssetText.IsVisible = asset is not null;
        NotesBox.Text = result.Notes ?? "";
        NotesBox.IsVisible = available && NotesBox.Text.Length > 0;
        DownloadButton.IsVisible = SkipButton.IsVisible = available;
    }

    /// <summary>The release file for this computer, or null where no package is published.</summary>
    static string? AssetFor(Version latest)
    {
        try { return UpdateAssets.FileName(latest, OsInfo.Current, RuntimeInformation.OSArchitecture); }
        catch (PlatformNotSupportedException) { return null; }
    }

    void AutoCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        _settings.CheckUpdatesOnStartup = AutoCheckBox.IsChecked == true;
        _save?.Invoke();
    }

    // Opens the release page in the browser; never downloads anything itself.
    void Download_Click(object? sender, RoutedEventArgs e)
    {
        if (_checker is null) return;
        _ = Links.OpenAsync(this, _result?.Url ?? _checker.ReleasesPage);
    }

    void Skip_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        _settings.SkippedVersion = _result?.Latest?.ToString();
        _save?.Invoke();
        Close();
    }

    async void Retry_Click(object? sender, RoutedEventArgs e) => await CheckAsync();

    // Avalonia's IsCancel only clicks the button; closing is ours.
    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
