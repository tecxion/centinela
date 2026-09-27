using System.Net;
using System.Text;
using Centinela.Core;

namespace Centinela.Core.Tests;

public class UpdateCheckerTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return respond(request, ct);
        }
    }

    static readonly Version Current = new(1, 2, 0);

    static (UpdateChecker, FakeHandler) With(HttpStatusCode code, string body = "")
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        return (new UpdateChecker(new HttpClient(handler)), handler);
    }

    static string Release(string tag, string url = "https://github.com/tecxion/centinela/releases/tag/v1.3.0") => $$"""
        { "tag_name": "{{tag}}", "name": "Centinela {{tag}}", "body": "- Novedades", "html_url": "{{url}}",
          "published_at": "2026-10-01T12:00:00Z" }
        """;

    [Fact]
    public async Task Newer_release_is_available_with_details()
    {
        var (checker, handler) = With(HttpStatusCode.OK, Release("v1.3.0"));
        var r = await checker.CheckAsync(Current);
        Assert.Equal(UpdateStatus.UpdateAvailable, r.Status);
        Assert.Equal(new Version(1, 3, 0), r.Latest);
        Assert.Equal("Centinela v1.3.0", r.Title);
        Assert.Equal("- Novedades", r.Notes);
        Assert.Equal("https://github.com/tecxion/centinela/releases/tag/v1.3.0", r.Url);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), r.PublishedAt);
        Assert.Equal("https://api.github.com/repos/tecxion/centinela/releases/latest", handler.Last!.RequestUri!.ToString());
        Assert.Equal("Centinela/1.2.0", handler.Last.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", handler.Last.Headers.Accept.ToString());
        Assert.Null(handler.Last.Headers.Authorization);
    }

    [Theory]
    [InlineData("v1.2.0")] [InlineData("1.2")] [InlineData("v1.1.9")]
    public async Task Same_or_older_release_is_up_to_date(string tag)
    {
        var (checker, _) = With(HttpStatusCode.OK, Release(tag));
        Assert.Equal(UpdateStatus.UpToDate, (await checker.CheckAsync(Current)).Status);
    }

    [Theory]
    [InlineData("latest")] [InlineData("v1")] [InlineData("v1.2.3.4")] [InlineData("")]
    public async Task Invalid_tag_fails(string tag)
    {
        var (checker, _) = With(HttpStatusCode.OK, Release(tag));
        var r = await checker.CheckAsync(Current);
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.Equal("La versión publicada no tiene un formato válido.", r.Error);
    }

    [Fact]
    public async Task Foreign_html_url_is_replaced_by_the_releases_page()
    {
        var (checker, _) = With(HttpStatusCode.OK, Release("v2.0.0", "https://evil.example/download"));
        Assert.Equal("https://github.com/tecxion/centinela/releases/latest", (await checker.CheckAsync(Current)).Url);
    }

    [Fact]
    public async Task Not_found_means_no_releases()
    {
        var (checker, _) = With(HttpStatusCode.NotFound);
        Assert.Equal(UpdateStatus.NoReleases, (await checker.CheckAsync(Current)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Rate_limit_has_its_own_message(HttpStatusCode code)
    {
        var (checker, _) = With(code);
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "GitHub ha limitado las consultas; prueba más tarde."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Server_error_fails()
    {
        var (checker, _) = With(HttpStatusCode.BadGateway);
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "GitHub no responde (código 502)."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Network_failure_is_no_internet()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("dns"))));
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "Sin conexión a Internet."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Timeout_fails_without_throwing()
    {
        var handler = new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var checker = new UpdateChecker(new HttpClient(handler), timeout: TimeSpan.FromMilliseconds(100));
        var r = await checker.CheckAsync(Current);
        Assert.Equal((UpdateStatus.Failed, "GitHub no responde (tiempo agotado)."), (r.Status, r.Error));
    }

    [Fact]
    public async Task Garbage_json_fails()
    {
        var (checker, _) = With(HttpStatusCode.OK, "<html>");
        Assert.Equal("Respuesta de GitHub no válida.", (await checker.CheckAsync(Current)).Error);
    }

    [Theory]
    [InlineData("v1.3.0", "1.3.0")] [InlineData("V2.0", "2.0.0")] [InlineData(" 1.10.2 ", "1.10.2")]
    public void ParseTag_normalizes(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));
}
