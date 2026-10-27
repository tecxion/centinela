using Avalonia.Threading;
using Centinela.Core;
using Centinela.Desktop.Services.Errors;
using Centinela.Desktop.Services.Ui;
using Centinela.Media;

namespace Centinela.Desktop.Services.Cameras;

/// <summary>
/// «Probar» in the camera dialog: opens the substream (decoded small, for the preview) and the main stream (not
/// decoded, only for its info) for at most 10 s and reports one line for each. UI thread only.
/// </summary>
sealed class CameraTest
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    StreamSession? _sub;     // substream: decoded, shown in the preview
    StreamSession? _main;    // main stream: not decoded, only for its info

    public CameraTest() => _timer.Tick += (_, _) => Finish();

    /// <summary>The session the preview shows; kept after the test finishes so its last frame stays.</summary>
    public StreamSession? Preview => _sub;

    public string MainLine { get; private set; } = "";
    public string SubLine { get; private set; } = "";

    /// <summary>«Conectando…» over the preview until the substream answers or the test ends; "" then.</summary>
    public string Status { get; private set; } = "";

    /// <summary>A result line changed.</summary>
    public event Action? LinesChanged;

    /// <summary><see cref="Status"/> changed.</summary>
    public event Action? StatusChanged;

    public void Start(Camera camera)
    {
        Stop();
        // A snapshot: the dialog keeps editing its own copy.
        camera = camera.Clone();
        MainLine = "Principal: conectando…";
        SubLine = "Secundaria: conectando…";
        LinesChanged?.Invoke();
        SetStatus("Conectando…");
        _sub = StartSession(camera, StreamKind.Sub, decode: true, line => SubLine = line, "Secundaria");
        _main = StartSession(camera, StreamKind.Main, decode: false, line => MainLine = line, "Principal");
        _timer.Start();
    }

    StreamSession StartSession(Camera camera, StreamKind kind, bool decode, Action<string> setLine, string label)
    {
        var session = new StreamSession(StreamUrlBuilder.Build(camera, kind), camera.UseUdp, decode);
        if (decode) session.SetTargetSize(640, 360);
        // Callbacks arrive on the session thread; ignored once the test was replaced, finished or stopped.
        session.InfoAvailable += info => UiThread.Post(() => Report(session, decode, setLine, $"{label}: {info.Describe()}"));
        // Never err.Detail: only the translated, user-facing text.
        session.ErrorOccurred += err => UiThread.Post(() =>
            Report(session, decode, setLine, $"{label}: {ErrorText.Translate(camera, err.Kind).Short}"));
        session.Start();
        return session;
    }

    void Report(StreamSession session, bool decode, Action<string> setLine, string line)
    {
        if (!IsCurrent(session)) return;
        setLine(line);
        if (decode) SetStatus("");
        LinesChanged?.Invoke();
    }

    // The timer runs only while a test is in progress: Finish keeps _sub for the preview, but its results are final.
    bool IsCurrent(StreamSession session) => _timer.IsEnabled && (_sub == session || _main == session);

    /// <summary>After 10 s: stop both sessions (the preview keeps its last frame) and mark what never answered.</summary>
    void Finish()
    {
        _timer.Stop();
        if (MainLine.EndsWith("conectando…")) MainLine = "Principal: sin respuesta en 10 s";
        if (SubLine.EndsWith("conectando…")) SubLine = "Secundaria: sin respuesta en 10 s";
        LinesChanged?.Invoke();
        if (Status == "Conectando…") SetStatus("");
        DisposeSessions();
        _main = null;
        // _sub stays referenced so the preview keeps showing its last frame; it is already stopping.
    }

    /// <summary>Stops everything (a new test, or the dialog closed).</summary>
    public void Stop()
    {
        _timer.Stop();
        DisposeSessions();
        _sub = _main = null;
    }

    // Stopping joins the session thread: keep that off the UI thread (a second Dispose is a no-op).
    void DisposeSessions()
    {
        foreach (var session in new[] { _sub, _main })
            if (session is not null) _ = Task.Run(session.Dispose);
    }

    void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }
}
