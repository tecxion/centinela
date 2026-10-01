using System.Net.Sockets;
using System.Text;
using Centinela.Core.Platform;

namespace Centinela.Platform.Unix;

/// <summary>
/// A Unix domain socket marks the first instance (Linux, and macOS later): a later launch connects, sends
/// <c>show</c> and exits. A CENTINELA_DATA_DIR run passes its own suffix to <see cref="PathFor"/>, so a test build
/// never wakes (or yields to) the user's instance.
/// </summary>
/// <remarks>
/// The claim binds first and deletes the file only when nobody answers on it, so two simultaneous launches cannot
/// both win while a live instance or a fresh socket owns the path. A tiny race remains after a crash: two launches
/// that both find the abandoned file can both delete it and bind in turn (or one can hit the instant between the
/// other's bind and listen). Closing it fully needs an <c>flock</c> on a separate lock file.
/// </remarks>
public sealed class UnixSingleInstance(string socketPath) : ISingleInstance, IDisposable
{
    const string ShowCommand = "show";
    const int MaxMessageBytes = 64;
    static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(2);
    static readonly TimeSpan AcceptRetryPause = TimeSpan.FromMilliseconds(200);
    readonly object _gate = new();
    Socket? _listener;
    bool? _claimed;
    bool _listening;
    volatile bool _disposed;

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
    /// <remarks>A repeated call returns the first result; after <see cref="Dispose"/> it never binds again.</remarks>
    public bool TryClaim()
    {
        lock (_gate)
        {
            if (_claimed is { } claimed) return claimed;
            if (_disposed) return false;
            _claimed = Claim();
            return _claimed.Value;
        }
    }

    bool Claim()
    {
        try
        {
            var folder = Path.GetDirectoryName(socketPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            if (TryBind()) return true;
            // The path is taken: a live instance answers; otherwise the file was abandoned by a crash.
            if (TrySignal()) return false;
            File.Delete(socketPath);
            if (TryBind()) return true;
            // Another launch bound it between our delete and our bind: it won, so wake it instead.
            if (TrySignal()) return false;
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            // An unwritable folder, a path too long for a socket, ...: fall through.
        }
        // Open anyway, without single-instance, rather than never open.
        return true;
    }

    /// <summary>True when this instance now listens on the path; false when the path is already in use.</summary>
    bool TryBind()
    {
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(backlog: 8);
            _listener = listener;
            return true;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            listener.Dispose();
            return false;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    /// <summary>True when an instance is listening at the path and received <c>show</c>.</summary>
    bool TrySignal()
    {
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
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException
                                      or ArgumentException)
        {
            // Nobody listening (an abandoned file) or a hung instance: treat it as no instance.
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
            // Claimed without a socket (the fallback above): there is nothing to listen on.
            if (_listener is null || _listening || _disposed) return;
            _listening = true;
            listener = _listener;
        }
        new Thread(() => AcceptLoop(listener, onActivate)) { IsBackground = true, Name = "SingleInstance" }.Start();
    }

    void AcceptLoop(Socket listener, Action onActivate)
    {
        while (!_disposed)
        {
            Socket connection;
            try
            {
                connection = listener.Accept();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (_disposed) return;
                // A transient failure (e.g. out of descriptors): keep the listener alive.
                Thread.Sleep(AcceptRetryPause);
                continue;
            }
            using (connection)
                ReadCommands(connection, onActivate);
        }
    }

    static void ReadCommands(Socket connection, Action onActivate)
    {
        var buffer = new byte[MaxMessageBytes];
        var length = 0;
        try
        {
            connection.ReceiveTimeout = (int)SignalTimeout.TotalMilliseconds;
            int read;
            while (length < buffer.Length && (read = connection.Receive(buffer, length, buffer.Length - length,
                       SocketFlags.None)) > 0)
                length += read;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // A launch that never finished (or hung): act on what arrived and keep accepting.
        }
        foreach (var line in Encoding.ASCII.GetString(buffer, 0, length).Split('\n'))
        {
            if (line.Trim() != ShowCommand) continue;
            try
            {
                onActivate();
            }
            catch (Exception)
            {
                // A failing handler must not take the process down with this background thread.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // Only the owner removes the socket: a later instance never deletes the winner's file.
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
