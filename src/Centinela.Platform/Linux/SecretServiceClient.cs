using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Tmds.DBus.Protocol;

namespace Centinela.Platform.Linux;

/// <summary>
/// The Secret Service (org.freedesktop.secrets: GNOME Keyring, KWallet) over the session bus, with a "plain"
/// session (the bus is private to the user). Items carry <see cref="SecretServiceNames.Attributes"/>. A locked
/// keychain is unlocked through the system's own prompt (at most <see cref="PromptTimeout"/>; not shown again once
/// turned down). Never throws: any failure is false/null, and the store then falls back to its file. A service that
/// did not answer in time is not called again until one of its replies arrives (see <see cref="ServiceTimeouts"/>).
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class SecretServiceClient(SessionBus bus) : ISecretBackend
{
    const string Service = "org.freedesktop.secrets";
    const string ServicePath = "/org/freedesktop/secrets";
    const string ServiceInterface = "org.freedesktop.Secret.Service";
    const string CollectionInterface = "org.freedesktop.Secret.Collection";
    const string ItemInterface = "org.freedesktop.Secret.Item";
    const string PromptInterface = "org.freedesktop.Secret.Prompt";
    const string NoObject = "/";
    const string ContentType = "text/plain; charset=utf8";
    static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(60);
    /// <summary>A prompt: subscribing, showing it, waiting for the user and, on time out, dismissing it.</summary>
    static readonly TimeSpan PromptBudget = PromptTimeout + 3 * SessionBus.CallTimeout;
    /// <summary>
    /// A whole operation covers its steps (Store, the longest: up to two prompts and five calls), so the inner time
    /// limits fire first; past it the operation is cancelled and never acts after its caller gave up.
    /// </summary>
    static readonly TimeSpan OperationTimeout = 2 * PromptBudget + 6 * SessionBus.CallTimeout;

    readonly PromptRefusal _refusal = new();
    readonly ServiceTimeouts _timeouts = new();

    public bool Store(Guid cameraId, string password) =>
        Run((connection, token) => StoreAsync(connection, cameraId, password, token));

    public string? Lookup(Guid cameraId) =>
        Run((connection, token) => LookupAsync(connection, cameraId, token));

    public void Delete(Guid cameraId) =>
        Run((connection, token) => DeleteAsync(connection, cameraId, token));

    /// <summary>An operation; at once false/null while the service is silent (it timed out and has not answered since).</summary>
    T? Run<T>(Func<Connection, CancellationToken, Task<T>> work) =>
        _timeouts.Silent
            ? default
            : bus.Run((connection, token) => _timeouts.Watch(() => work(connection, token)), OperationTimeout);

    async Task<bool> StoreAsync(Connection connection, Guid cameraId, string password, CancellationToken token)
    {
        var collection = await Call(connection, ReadAlias(connection, "default"), ReadObjectPath, token);
        if (collection == NoObject) return false; // no default keychain
        if (!await UnlockAsync(connection, [collection], token)) return false;
        var session = await OpenSessionAsync(connection, token);
        try
        {
            var secret = Encoding.UTF8.GetBytes(password);
            try
            {
                token.ThrowIfCancellationRequested();
                var (_, prompt) = await Call(connection, CreateItem(connection, collection, session, cameraId, secret),
                    ReadTwoObjectPaths, token);
                return prompt == NoObject || await PromptAsync(connection, prompt, token);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
        finally
        {
            await CloseSessionAsync(connection, session);
        }
    }

    async Task<string?> LookupAsync(Connection connection, Guid cameraId, CancellationToken token)
    {
        var item = (await FindItemsAsync(connection, cameraId, token)).FirstOrDefault();
        if (item is null) return null;
        var session = await OpenSessionAsync(connection, token);
        try
        {
            var secret = await Call(connection, Simple(connection, item, ItemInterface, "GetSecret", "o", session),
                ReadSecretValue, token);
            try
            {
                return Encoding.UTF8.GetString(secret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
        finally
        {
            await CloseSessionAsync(connection, session);
        }
    }

    async Task<bool> DeleteAsync(Connection connection, Guid cameraId, CancellationToken token)
    {
        foreach (var item in await FindItemsAsync(connection, cameraId, token))
        {
            var prompt = await Call(connection, Simple(connection, item, ItemInterface, "Delete"), ReadObjectPath, token);
            if (prompt != NoObject && !await PromptAsync(connection, prompt, token)) return false;
        }
        return true;
    }

    /// <summary>The camera's items, unlocked (with the system prompt when needed). Empty when none or still locked.</summary>
    async Task<string[]> FindItemsAsync(Connection connection, Guid cameraId, CancellationToken token)
    {
        var (unlocked, locked) = await Call(connection, SearchItems(connection, cameraId), ReadTwoPathArrays, token);
        if (locked.Length == 0) return unlocked;
        return await UnlockAsync(connection, locked, token) ? [.. unlocked, .. locked] : unlocked;
    }

    /// <summary>Unlocks the objects (no prompt when they already are). False when the prompt was turned down.</summary>
    async Task<bool> UnlockAsync(Connection connection, string[] objects, CancellationToken token)
    {
        var (_, prompt) = await Call(connection, Unlock(connection, objects), ReadPathArrayAndPath, token);
        if (prompt != NoObject) return await PromptAsync(connection, prompt, token);
        _refusal.Unlocked(); // unlocked meanwhile (e.g. from the keychain app): prompts are welcome again
        return true;
    }

    /// <summary>
    /// Shows a Secret Service prompt and waits for its Completed signal. True when it was accepted. Once one was
    /// dismissed or timed out, no other is shown (see <see cref="PromptRefusal"/>).
    /// </summary>
    async Task<bool> PromptAsync(Connection connection, string prompt, CancellationToken token)
    {
        if (!_refusal.MayPrompt)
        {
            await DismissAsync(connection, prompt); // never shown: let the service drop it
            return false;
        }
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = new MatchRule { Type = MessageType.Signal, Path = prompt, Interface = PromptInterface, Member = "Completed" };
        // Subscribed before Prompt is called, so the signal cannot be missed.
        using var subscription = await connection.AddMatchAsync(rule,
            static (Message message, object? _) => message.GetBodyReader().ReadBool(),
            (Exception? error, bool dismissed, object? _, object? _) => completed.TrySetResult(error is null && !dismissed),
            ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false)
            .AsTask().WaitAsync(SessionBus.CallTimeout, token);
        // Window id "": the prompt is not tied to one of our windows.
        await connection.CallMethodAsync(Simple(connection, prompt, PromptInterface, "Prompt", "s", ""))
            .WaitAsync(SessionBus.CallTimeout, token);
        bool accepted;
        try
        {
            accepted = await completed.Task.WaitAsync(PromptTimeout, token);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            await DismissAsync(connection, prompt);
            accepted = false;
        }
        _refusal.Record(accepted);
        return accepted;
    }

    static Task DismissAsync(Connection connection, string prompt) =>
        BestEffort(connection, Simple(connection, prompt, PromptInterface, "Dismiss")); // it may already be gone

    static Task<string> OpenSessionAsync(Connection connection, CancellationToken token) =>
        Call(connection, OpenSession(connection), static (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadVariantValue(); // "plain" has no output
            return reader.ReadObjectPathAsString();
        }, token);

    /// <summary>The service also drops it with our connection.</summary>
    static Task CloseSessionAsync(Connection connection, string session) =>
        BestEffort(connection, Simple(connection, session, "org.freedesktop.Secret.Session", "Close"));

    /// <summary>A call whose failure does not matter. Without the operation's token: it also runs after a cancellation.</summary>
    static async Task BestEffort(Connection connection, MessageBuffer message)
    {
        try
        {
            await connection.CallMethodAsync(message).WaitAsync(SessionBus.CallTimeout);
        }
        catch (Exception)
        {
            // Nothing to do.
        }
    }

    static Task<T> Call<T>(Connection connection, MessageBuffer message, MessageValueReader<T> reader,
        CancellationToken token) =>
        connection.CallMethodAsync(message, reader).WaitAsync(SessionBus.CallTimeout, token);
}
