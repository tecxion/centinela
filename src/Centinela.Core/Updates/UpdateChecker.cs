using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centinela.Core;

public enum UpdateStatus { UpToDate, UpdateAvailable, NoReleases, Failed }

public sealed record UpdateResult(UpdateStatus Status, Version? Latest = null, string? Title = null, string? Notes = null,
    string? Url = null, DateTimeOffset? PublishedAt = null, string? Error = null);

/// <summary>
/// Asks GitHub for the latest published release. Sends only the app version as User-Agent. Network, timeout and
/// JSON errors come back as <see cref="UpdateStatus.Failed"/>; the caller's own cancellation propagates, and anything
/// unexpected can still throw, so callers catch.
/// </summary>
public sealed class UpdateChecker(HttpClient http, string repository = UpdateChecker.DefaultRepository, TimeSpan? timeout = null)
{
    public const string DefaultRepository = "tecxion/centinela";
    readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);

    public string ReleasesPage => $"https://github.com/{repository}/releases/latest";

    public async Task<UpdateResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Centinela", Normalize(current).ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        try
        {
            using var response = await http.SendAsync(request, cts.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateResult(UpdateStatus.NoReleases);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed("GitHub ha limitado las consultas; prueba más tarde.");
            if (!response.IsSuccessStatusCode) return Failed($"GitHub no responde (código {(int)response.StatusCode}).");
            var release = JsonSerializer.Deserialize<Release>(await response.Content.ReadAsStringAsync(cts.Token));
            if (ParseTag(release?.TagName) is not { } latest) return Failed("La versión publicada no tiene un formato válido.");
            var url = release!.HtmlUrl is { } html && html.StartsWith($"https://github.com/{repository}/", StringComparison.OrdinalIgnoreCase)
                ? html : ReleasesPage;
            var status = latest > Normalize(current) ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
            return new UpdateResult(status, latest, release.Name, release.Body, url, release.PublishedAt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("GitHub no responde (tiempo agotado).");
        }
        catch (HttpRequestException)
        {
            return Failed("Sin conexión a Internet.");
        }
        catch (JsonException)
        {
            return Failed("Respuesta de GitHub no válida.");
        }
    }

    /// <summary>"v1.3.0", "1.3" → 1.3.0; anything else (1 or 4 components, text) → null.</summary>
    public static Version? ParseTag(string? tag)
    {
        var text = tag?.Trim() ?? "";
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return Version.TryParse(text, out var v) && v.Revision == -1 ? Normalize(v) : null;
    }

    /// <summary>major.minor.patch, so 1.2 and 1.2.0 compare equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static UpdateResult Failed(string error) => new(UpdateStatus.Failed, Error: error);

    sealed class Release
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    }
}
