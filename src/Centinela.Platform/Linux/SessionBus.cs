using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace Centinela.Platform.Linux;

/// <summary>
/// The D-Bus session bus, connected on first use (at most <see cref="ConnectTimeout"/>). Unavailable when there is
/// no session bus (no DBUS_SESSION_BUS_ADDRESS, or it does not answer): callers then do without it. Never throws.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SessionBus : IDisposable
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    /// <summary>The longest a single D-Bus call may take.</summary>
    internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    readonly Lazy<Connection?> _connection = new(Connect, LazyThreadSafetyMode.ExecutionAndPublication);

    public bool IsAvailable => Connection is not null;

    /// <summary>The connection, or null when there is no session bus.</summary>
    internal Connection? Connection => _connection.Value;

    /// <summary>Whether some process owns the bus name right now (false without a session bus).</summary>
    public bool HasOwner(string name) =>
        Run((connection, token) => connection.CallMethodAsync(NameHasOwner(connection, name),
            static (Message message, object? _) => message.GetBodyReader().ReadBool()).WaitAsync(token), CallTimeout);

    /// <summary>
    /// Runs <paramref name="work"/> on the thread pool (never on the caller's UI context) and waits at most
    /// <paramref name="timeout"/>. Any failure, a timeout or no session bus gives default. On a timeout the token is
    /// cancelled, so the work stops instead of acting after its caller gave up.
    /// </summary>
    internal T? Run<T>(Func<Connection, CancellationToken, Task<T>> work, TimeSpan timeout)
    {
        if (Connection is not { } connection) return default;
        var cancel = new CancellationTokenSource();
        try
        {
            var task = Task.Run(() => work(connection, cancel.Token), cancel.Token);
            if (task.Wait(timeout))
            {
                cancel.Dispose(); // only once the work is done with its token
                return task.Result;
            }
            cancel.Cancel();
            return default;
        }
        catch (Exception)
        {
            return default; // AggregateException from the call (D-Bus error, disconnection, ...)
        }
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated) _connection.Value?.Dispose();
    }

    static Connection? Connect()
    {
        if (Address.Session is not { Length: > 0 } address) return null;
        Connection? connection = null;
        try
        {
            connection = new Connection(address); // a malformed address throws here: no bus
            var connect = Task.Run(async () => await connection.ConnectAsync());
            if (connect.Wait(ConnectTimeout)) return connection;
        }
        catch (Exception)
        {
            // No bus at that address, or it refused us.
        }
        connection?.Dispose();
        return null;
    }

    static MessageBuffer NameHasOwner(Connection connection, string name)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus", member: "NameHasOwner", signature: "s");
            writer.WriteString(name);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
