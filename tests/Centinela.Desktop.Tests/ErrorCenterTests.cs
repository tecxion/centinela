using Centinela.Core;
using Centinela.Desktop.Services.Errors;
using Centinela.Desktop.Services.Notices;

namespace Centinela.Desktop.Tests;

/// <summary>The error center: every error is logged; notices follow the policy and the notification gate.</summary>
public sealed class ErrorCenterTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "centinela-desktop-" + Guid.NewGuid());
    readonly ErrorLog _log;
    readonly List<Toast> _toasts = [];
    readonly List<NoticeKind> _sounds = [];
    readonly List<int> _unread = [];
    readonly List<(NoticeKind Kind, Camera Camera)> _asked = [];
    readonly Camera _camera = new() { Name = "Patio", Password = "secreta", Brand = Brand.Custom };
    NoticeDecision _decision = new(Show: true, PlaySound: true);

    public ErrorCenterTests() => _log = new ErrorLog(_dir);

    public void Dispose()
    {
        _log.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    ErrorCenter NewCenter()
    {
        var center = new ErrorCenter(_log, (kind, camera) =>
        {
            _asked.Add((kind, camera));
            return _decision;
        });
        center.ToastRequested += _toasts.Add;
        center.SoundRequested += _sounds.Add;
        center.UnreadChanged += _unread.Add;
        return center;
    }

    static StreamError Error(StreamErrorKind kind, string detail = "detalle") => new(kind, null, 0, detail, DateTime.UtcNow);

    [Fact]
    public void An_error_is_logged_and_counts_as_unread()
    {
        var center = NewCenter();
        center.Report(_camera, Error(StreamErrorKind.AuthFailed, "401"));
        var entry = Assert.Single(_log.Snapshot());
        var text = ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Custom, "Patio");
        Assert.Equal(("Patio", text.Short, text.Title, "401"), (entry.Camera, entry.Kind, entry.Title, entry.Detail));
        Assert.Equal(1, center.Unread);
        Assert.Equal([1], _unread);
    }

    [Fact]
    public void Two_equal_errors_in_a_row_notify_once()
    {
        var center = NewCenter();
        center.Report(_camera, Error(StreamErrorKind.AuthFailed));
        center.Report(_camera, Error(StreamErrorKind.AuthFailed));
        Assert.Equal(2, _log.Snapshot().Count);
        Assert.Equal(2, center.Unread);
        var toast = Assert.Single(_toasts);
        var text = ErrorTranslator.Translate(StreamErrorKind.AuthFailed, Brand.Custom, "Patio");
        Assert.Equal((text.Title, text.Advice, ToastStyle.Error), (toast.Title, toast.Message, toast.Style));
        Assert.Equal([NoticeKind.ConnectionLost], _sounds);
        Assert.Equal((NoticeKind.ConnectionLost, _camera), Assert.Single(_asked));
    }

    [Fact]
    public void A_gate_that_does_not_show_still_logs()
    {
        _decision = new NoticeDecision(Show: false, PlaySound: false);
        var center = NewCenter();
        center.Report(_camera, Error(StreamErrorKind.NotFound));
        Assert.Single(_log.Snapshot());
        Assert.Empty(_toasts);
        Assert.Empty(_sounds);
        Assert.Equal(1, center.Unread);
    }

    [Fact]
    public void Playing_after_a_notified_error_logs_and_announces_the_recovery()
    {
        var center = NewCenter();
        center.Report(_camera, Error(StreamErrorKind.CameraBusy));
        _toasts.Clear();
        _sounds.Clear();
        center.Playing(_camera);
        var entry = _log.Snapshot()[0];
        Assert.Equal(("Patio", "Recuperada", "Patio: conexión recuperada", ""), (entry.Camera, entry.Kind, entry.Title, entry.Detail));
        var toast = Assert.Single(_toasts);
        Assert.Equal(("✓ Patio: conexión recuperada", "", ToastStyle.Recovery), (toast.Title, toast.Message, toast.Style));
        Assert.Equal(NoticeKind.ConnectionRecovered, _asked[^1].Kind);
        Assert.Empty(_sounds);
    }

    [Fact]
    public void Playing_without_a_notified_error_says_nothing()
    {
        var center = NewCenter();
        center.Playing(_camera);
        Assert.Empty(_log.Snapshot());
        Assert.Empty(_toasts);
    }

    [Fact]
    public void Forget_lets_the_same_error_notify_again()
    {
        var center = NewCenter();
        center.Report(_camera, Error(StreamErrorKind.AuthFailed));
        center.Forget(_camera.Id);
        center.Report(_camera, Error(StreamErrorKind.AuthFailed));
        Assert.Equal(2, _toasts.Count);
    }

    [Fact]
    public void Mark_read_resets_the_count()
    {
        var center = NewCenter();
        center.Report(_camera, Error(StreamErrorKind.Unreachable));
        center.MarkRead();
        Assert.Equal(0, center.Unread);
        Assert.Equal([1, 0], _unread);
    }

    [Fact]
    public void Auth_failure_without_a_stored_password_is_logged_as_missing()
    {
        var center = NewCenter();
        center.Report(new Camera { Name = "Garaje", Password = "" }, Error(StreamErrorKind.AuthFailed));
        var entry = Assert.Single(_log.Snapshot());
        Assert.Equal(("Falta contraseña", "Garaje: falta la contraseña"), (entry.Kind, entry.Title));
        Assert.Equal("Pulsa «Editar» en la cámara y escribe su contraseña.", Assert.Single(_toasts).Message);
    }
}
