using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Centinela.Core.Platform;
using Tmds.DBus.Protocol;

namespace Centinela.Platform.Linux;

/// <summary>
/// Desktop notifications over org.freedesktop.Notifications on the session bus. <see cref="Show"/> returns at once:
/// the call is sent from the thread pool. A click on the notification (its "default" action) runs the click action
/// on the UI thread (<paramref name="ui"/>). Without a session bus or a notification service nothing shows. Never
/// throws.
/// </summary>
/// <remarks>
/// Actions are kept by (server's unique bus name, notification id) until the server reports the notification
/// closed: a signal from another process, or from a restarted server that reuses ids, never runs one.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxNotifier(SessionBus bus, SynchronizationContext ui) : INotifier
{
    const string Service = "org.freedesktop.Notifications";
    const string ServicePath = "/org/freedesktop/Notifications";

    readonly ConcurrentDictionary<(string Server, uint Id), Action> _actions = new();
    readonly Lock _gate = new();
    readonly List<IDisposable> _subscriptions = [];
    Task<bool>? _subscribing;
    volatile bool _disposed;

    public void Show(string title, string text, Action? onClick = null)
    {
        if (_disposed) return;
        var request = NotificationRequest.Create(title, text);
        _ = Task.Run(() => ShowAsync(request, onClick)); // connecting and calling never block the UI thread
    }

    public void Dispose()
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }
        foreach (var subscription in subscriptions) subscription.Dispose();
        _actions.Clear();
    }

    async Task ShowAsync(NotificationRequest request, Action? onClick)
    {
        try
        {
            if (bus.Connection is not { } connection) return;
            // Subscribed before Notify, so a click cannot come before we listen. Without the signals the
            // notification still shows; only its click does nothing.
            var listening = onClick is not null && await SubscribeAsync(connection);
            var (server, id) = await connection.CallMethodAsync(Notify(connection, request),
                static (Message message, object? _) => (message.SenderAsString ?? "", message.GetBodyReader().ReadUInt32()))
                .WaitAsync(SessionBus.CallTimeout);
            if (listening && !_disposed) _actions[(server, id)] = onClick!;
        }
        catch (Exception)
        {
            // No notification service, or it did not answer in time: nothing shows.
        }
    }

    /// <summary>Subscribes once to ActionInvoked and NotificationClosed (again after a failed attempt).</summary>
    Task<bool> SubscribeAsync(Connection connection)
    {
        lock (_gate)
        {
            if (_subscribing is null || _subscribing is { IsCompleted: true, Result: false })
                _subscribing = AddMatchesAsync(connection);
            return _subscribing;
        }
    }

    async Task<bool> AddMatchesAsync(Connection connection)
    {
        var added = new List<IDisposable>();
        try
        {
            added.Add(await connection.AddMatchAsync(Rule("ActionInvoked"),
                static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    return (Server: message.SenderAsString ?? "", Id: reader.ReadUInt32(), Key: reader.ReadString());
                },
                (Exception? error, (string Server, uint Id, string Key) signal, object? _, object? _) =>
                {
                    if (error is null) OnActionInvoked(signal.Server, signal.Id, signal.Key);
                },
                ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false)
                .AsTask().WaitAsync(SessionBus.CallTimeout));
            added.Add(await connection.AddMatchAsync(Rule("NotificationClosed"),
                static (Message message, object? _) => (Server: message.SenderAsString ?? "", Id: message.GetBodyReader().ReadUInt32()),
                (Exception? error, (string Server, uint Id) signal, object? _, object? _) =>
                {
                    if (error is null) _actions.TryRemove(signal, out var _);
                },
                ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false)
                .AsTask().WaitAsync(SessionBus.CallTimeout));
            lock (_gate)
            {
                if (!_disposed)
                {
                    _subscriptions.AddRange(added);
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Not subscribed: clicks do nothing this time.
        }
        foreach (var subscription in added) subscription.Dispose();
        return false;
    }

    /// <summary>On a signal thread: only a click on the notification itself, from the server that showed it.</summary>
    void OnActionInvoked(string server, uint id, string key)
    {
        if (key != NotificationRequest.DefaultAction || _disposed) return;
        if (!_actions.TryGetValue((server, id), out var action)) return;
        ui.Post(_ =>
        {
            if (!_disposed) action();
        }, null);
    }

    static MatchRule Rule(string member) =>
        new() { Type = MessageType.Signal, Path = ServicePath, Interface = Service, Member = member };

    /// <summary>Notify(app_name s, replaces_id u, app_icon s, summary s, body s, actions as, hints a{sv}, expire_timeout i) → id u.</summary>
    static MessageBuffer Notify(Connection connection, NotificationRequest request)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Service, ServicePath, Service, "Notify", "susssasa{sv}i");
            writer.WriteString(request.AppName);
            writer.WriteUInt32(request.ReplacesId);
            writer.WriteString(request.AppIcon);
            writer.WriteString(request.Summary);
            writer.WriteString(request.Body);
            writer.WriteArray(request.Actions.ToArray());
            writer.WriteDictionaryEnd(writer.WriteDictionaryStart()); // no hints
            writer.WriteInt32(request.ExpireTimeout);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
