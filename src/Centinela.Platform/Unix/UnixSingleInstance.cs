using System.Net.Sockets;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform.Unix;

/// <summary>
/// A Unix domain socket marks the first instance (Linux, and macOS later): a later launch connects, sends
/// <c>show</c> and exits. A CENTINELA_DATA_DIR run passes its own suffix to <see cref="PathFor"/>, so a test build
/// never wakes (or yields to) the user's instance.
/// </summary>
public sealed class UnixSingleInstance(string socketPath) : ISingleInstance, IDisposable
{
    const string ShowCommand = "show";
    static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(2);
    readonly object _gate = new();
    Socket? _listener;
    bool? _claimed;
    bool _listening;
    bool _disposed;

    /// <summary>
    /// <c>$XDG_RUNTIME_DIR/centinela{suffix}.sock</c> when <paramref name="runtimeDir"/> is an absolute path;
    /// otherwise <c>&lt;dataDir&gt;/centinela{suffix}.sock</c>.
    /// </summary>
    public static string PathFor(string? runtimeDir, string dataDir, string suffix)
    {
        var name = $"centinela{suffix}.sock";
        var folder = !string.IsNullOrEmpty(runtimeDir) && Path.IsPathRooted(runtimeDir) ? runtimeDir : dataDir;
        return Path.Combine(folder, name);
    }

    /// <inheritdoc/>
    /// <remarks>A repeated call returns the first result.</remarks>
    public bool TryClaim()
    {
        lock (_gate) return _claimed ??= Claim();
    }

    bool Claim()
    {
        if (TrySignal()) return false;
        if (TryListen()) return true;
        // Another launch bound the path between our connect and our bind: it won, so wake it instead.
        if (TrySignal()) return false;
        // Neither bind nor connect works (e.g. an unwritable folder): open anyway, without single-instance.
        return true;
    }

    /// <summary>True when an instance is listening at the path and received <c>show</c>.</summary>
    bool TrySignal()
    {
        if (!File.Exists(socketPath)) return false;
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using (var timeout = new CancellationTokenSource(SignalTimeout))
                client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeout.Token)
                    .AsTask().GetAwaiter().GetResult();
            client.SendTimeout = (int)SignalTimeout.TotalMilliseconds;
            client.Send(Encoding.ASCII.GetBytes(ShowCommand + "\n"));
            client.Shutdown(SocketShutdown.Send);
            return true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException)
        {
            // Nobody listening (an abandoned file) or a hung instance: treat it as no instance.
            return false;
        }
    }

    bool TryListen()
    {
        var folder = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            // Left behind by an instance that crashed; nobody answered the connect above.
            File.Delete(socketPath);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(backlog: 8);
            _listener = listener;
            return true;
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            listener.Dispose();
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Only the first call listens; later calls keep that listener.</remarks>
    public void ListenForActivation(Action onActivate)
    {
        Socket listener;
        lock (_gate)
        {
            if (_claimed != true) throw new InvalidOperationException("Not the first instance.");
            // Claimed without a socket (bind and connect both failed): there is nothing to listen on.
            if (_listener is null || _listening) return;
            _listening = true;
            listener = _listener;
        }
        new Thread(() => AcceptLoop(listener, onActivate)) { IsBackground = true, Name = "SingleInstance" }.Start();
    }

    static void AcceptLoop(Socket listener, Action onActivate)
    {
        while (true)
        {
            Socket connection;
            try
            {
                connection = listener.Accept();
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                // The instance was disposed: stop listening.
                return;
            }
            using (connection)
                ReadCommands(connection, onActivate);
        }
    }

    static void ReadCommands(Socket connection, Action onActivate)
    {
        try
        {
            connection.ReceiveTimeout = (int)SignalTimeout.TotalMilliseconds;
            using var reader = new StreamReader(new NetworkStream(connection, ownsSocket: false), Encoding.ASCII);
            while (reader.ReadLine() is { } line)
                if (line.Trim() == ShowCommand) onActivate();
        }
        catch (IOException)
        {
            // A launch that never finished its line (or hung): ignore it and keep accepting.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_listener is null) return;
            _listener.Dispose();
            try
            {
                File.Delete(socketPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover file is harmless: the next launch finds nobody listening and replaces it.
            }
        }
    }
}
