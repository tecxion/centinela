using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Tmds.DBus.Protocol;

namespace Centinela.Platform.Linux;

/// <summary>
/// The Secret Service (org.freedesktop.secrets: GNOME Keyring, KWallet) over the session bus, with a "plain"
/// session (the bus is private to the user). Items carry <see cref="SecretServiceNames.Attributes"/>. A locked
/// keychain is unlocked through the system's own prompt (at most <see cref="PromptTimeout"/>; not shown again once
/// turned down). Never throws: any
/// failure is false/null, and the store then falls back to its file.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class SecretServiceClient(SessionBus bus) : ISecretBackend
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

    public bool Store(Guid cameraId, string password) =>
        bus.Run((connection, token) => StoreAsync(connection, cameraId, password, token), OperationTimeout);

    public string? Lookup(Guid cameraId) =>
        bus.Run((connection, token) => LookupAsync(connection, cameraId, token), OperationTimeout);

    public void Delete(Guid cameraId) =>
        bus.Run((connection, token) => DeleteAsync(connection, cameraId, token), OperationTimeout);

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

    // ---- Replies ----

    static string ReadObjectPath(Message message, object? _) => message.GetBodyReader().ReadObjectPathAsString();

    static (string, string) ReadTwoObjectPaths(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
    }

    static (string[], string[]) ReadTwoPathArrays(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        return (Paths(reader.ReadArrayOfObjectPath()), Paths(reader.ReadArrayOfObjectPath()));
    }

    static (string[], string) ReadPathArrayAndPath(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        return (Paths(reader.ReadArrayOfObjectPath()), reader.ReadObjectPathAsString());
    }

    /// <summary>Secret (oayays): session, parameters, value, content type. Only the value is kept.</summary>
    static byte[] ReadSecretValue(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        reader.AlignStruct();
        reader.ReadObjectPath();
        reader.ReadArrayOfByte();
        var value = reader.ReadArrayOfByte();
        reader.ReadString();
        return value;
    }

    static string[] Paths(ObjectPath[] paths) => [.. paths.Select(path => path.ToString())];

    // ---- Requests (MessageWriter is a ref struct: built here, outside the async methods) ----

    delegate void Body(ref MessageWriter writer);

    static MessageBuffer Request(Connection connection, string path, string @interface, string member,
        string? signature = null, Body? body = null)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Service, path, @interface, member, signature);
            body?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>A call with no argument, or one string ("s") or object path ("o") argument.</summary>
    static MessageBuffer Simple(Connection connection, string path, string @interface, string member,
        string? signature = null, string? argument = null) =>
        Request(connection, path, @interface, member, signature, (ref MessageWriter writer) =>
        {
            if (signature == "s") writer.WriteString(argument!);
            else if (signature == "o") writer.WriteObjectPath(argument!);
        });

    static MessageBuffer OpenSession(Connection connection) =>
        Request(connection, ServicePath, ServiceInterface, "OpenSession", "sv", (ref MessageWriter writer) =>
        {
            writer.WriteString("plain");
            writer.WriteVariantString("");
        });

    static MessageBuffer ReadAlias(Connection connection, string alias) =>
        Simple(connection, ServicePath, ServiceInterface, "ReadAlias", "s", alias);

    static MessageBuffer Unlock(Connection connection, string[] objects) =>
        Request(connection, ServicePath, ServiceInterface, "Unlock", "ao",
            (ref MessageWriter writer) => writer.WriteArray(objects.Select(path => new ObjectPath(path)).ToArray()));

    static MessageBuffer SearchItems(Connection connection, Guid cameraId) =>
        Request(connection, ServicePath, ServiceInterface, "SearchItems", "a{ss}", (ref MessageWriter writer) =>
        {
            var start = writer.WriteDictionaryStart();
            foreach (var (key, value) in SecretServiceNames.Attributes(cameraId))
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString(key);
                writer.WriteString(value);
            }
            writer.WriteDictionaryEnd(start);
        });

    /// <summary>CreateItem(properties a{sv}, secret (oayays), replace b): replaces this camera's earlier item.</summary>
    static MessageBuffer CreateItem(Connection connection, string collection, string session, Guid cameraId, byte[] secret) =>
        Request(connection, collection, CollectionInterface, "CreateItem", "a{sv}(oayays)b", (ref MessageWriter writer) =>
        {
            writer.WriteDictionary(new Dictionary<string, VariantValue>
            {
                [ItemInterface + ".Label"] = VariantValue.String(SecretServiceNames.Label(cameraId)),
                [ItemInterface + ".Attributes"] =
                    new Dict<string, string>(SecretServiceNames.Attributes(cameraId).ToDictionary()).AsVariantValue(),
            });
            writer.WriteStructureStart();
            writer.WriteObjectPath(session);
            writer.WriteArray(Array.Empty<byte>());
            writer.WriteArray(secret);
            writer.WriteString(ContentType);
            writer.WriteBool(true);
        });
}
