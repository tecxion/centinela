using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Centinela.Media.Tests.Rtsp;

[CollectionDefinition("rtsp")]
public sealed class RtspCollection : ICollectionFixture<RtspTestServer>;

/// <summary>mediamtx on :18554 with two looping 640x360 H.264 test streams: "open" (anonymous) and "secure" (viewer / p@ss#w/rd).</summary>
public sealed class RtspTestServer : IDisposable
{
    public const int Port = 18554;
    public const string SecureUser = "viewer";
    // mediamtx (v1.21) cannot authenticate passwords containing ':' (its Basic parser splits on every
    // colon), so the special characters exercised here are '@', '#' and '/', all of which need URL escaping.
    public const string SecurePassword = "p@ss#w/rd";

    // Plaintext passwords with '/' are rejected by mediamtx's config validation; a sha256 hash is accepted.
    static readonly string Config = $$"""
        logLevel: warn
        rtspAddress: :{{Port}}
        rtpAddress: :18000
        rtcpAddress: :18001
        rtmp: no
        hls: no
        webrtc: no
        srt: no
        moq: no
        api: no
        metrics: no
        pprof: no
        playback: no
        authInternalUsers:
          - user: any
            pass:
            ips: []
            permissions:
              - action: publish
              - action: read
                path: open
              - action: read
                path: missing
          - user: {{SecureUser}}
            pass: "sha256:{{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(SecurePassword)))}}"
            ips: []
            permissions:
              - action: read
                path: secure
        paths:
          all_others:
        """;

    readonly string? _serverExe;
    readonly string? _ffmpegExe;
    readonly string _configPath = Path.Combine(Path.GetTempPath(), $"centinela-mediamtx-{Guid.NewGuid()}.yml");
    readonly List<Process> _processes = [];

    public RtspTestServer()
    {
        _serverExe = Path.Combine(TestUtil.RepoRoot, "tools", "bin", "mediamtx.exe");
        _ffmpegExe = TestUtil.FindOnPath("ffmpeg.exe");
        if (!File.Exists(_serverExe)) { SkipReason = "mediamtx no encontrado: ejecuta tools/get-mediamtx.ps1"; return; }
        if (_ffmpegExe is null) { SkipReason = "ffmpeg.exe (con libx264) no está en el PATH"; return; }
        File.WriteAllText(_configPath, Config);
        StartAll();
    }

    public string? SkipReason { get; }

    public string Url(string path, string? user = null, string? password = null) =>
        user is null
            ? $"rtsp://127.0.0.1:{Port}/{path}"
            : $"rtsp://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password ?? "")}@127.0.0.1:{Port}/{path}";

    public void RestartServer()
    {
        KillAll();
        StartAll();
    }

    void StartAll()
    {
        _processes.Add(Launch(_serverExe!, $"\"{_configPath}\""));
        WaitForPort();
        foreach (var path in new[] { "open", "secure" })
            _processes.Add(Launch(_ffmpegExe!,
                "-hide_banner -loglevel error -re -f lavfi -i testsrc2=size=640x360:rate=25 " +
                "-c:v libx264 -preset ultrafast -tune zerolatency -g 25 -pix_fmt yuv420p " +
                $"-f rtsp -rtsp_transport tcp rtsp://127.0.0.1:{Port}/{path}"));
        Thread.Sleep(2000);
    }

    static Process Launch(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true })!;

    static void WaitForPort()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect("127.0.0.1", Port);
                return;
            }
            catch (SocketException) { Thread.Sleep(100); }
        }
        throw new TimeoutException("mediamtx did not open its RTSP port");
    }

    void KillAll()
    {
        foreach (var p in _processes)
        {
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } }
            catch (InvalidOperationException) { }
            p.Dispose();
        }
        _processes.Clear();
    }

    public void Dispose()
    {
        KillAll();
        if (File.Exists(_configPath)) File.Delete(_configPath);
    }
}
