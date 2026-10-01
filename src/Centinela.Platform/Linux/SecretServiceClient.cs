using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Tmds.DBus.Protocol;

namespace Centinela.Platform.Linux;

/// <summary>
/// The Secret Service (org.freedesktop.secrets: GNOME Keyring, KWallet) over the session bus, with a "plain"
/// session (the bus is private to the user). Items carry <see cref="SecretServiceNames.Attributes"/>. A locked
/// keychain is unlocked through the system's own prompt (at most <see cref="PromptTimeout"/>). Never throws: any
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
    /// <summary>A whole operation: a few calls plus, at most, one prompt.</summary>
    static readonly TimeSpan OperationTimeout = PromptTimeout + TimeSpan.FromSeconds(15);

    public bool Store(Guid cameraId, string password) =>
        bus.Run(connection => StoreAsync(connection, cameraId, password), OperationTimeout);

    public string? Lookup(Guid cameraId) => bus.Run(connection => LookupAsync(connection, cameraId), OperationTimeout);

    public void Delete(Guid cameraId) => bus.Run(connection => DeleteAsync(connection, cameraId), OperationTimeout);

    static async Task<bool> StoreAsync(Connection connection, Guid cameraId, string password)
    {
        var collection = await Call(connection, ReadAlias(connection, "default"), ReadObjectPath);
        if (collection == NoObject) return false; // no default keychain
        if (!await UnlockAsync(connection, [collection])) return false;
        var session = await OpenSessionAsync(connection);
        try
        {
            var secret = Encoding.UTF8.GetBytes(password);
            try
            {
                var (_, prompt) = await Call(connection, CreateItem(connection, collection, session, cameraId, secret),
                    ReadTwoObjectPaths);
                return prompt == NoObject || await PromptAsync(connection, prompt);
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

    static async Task<string?> LookupAsync(Connection connection, Guid cameraId)
    {
        var item = (await FindItemsAsync(connection, cameraId)).FirstOrDefault();
        if (item is null) return null;
        var session = await OpenSessionAsync(connection);
        try
        {
            var secret = await Call(connection, Simple(connection, item, ItemInterface, "GetSecret", "o", session),
                ReadSecretValue);
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

    static async Task<bool> DeleteAsync(Connection connection, Guid cameraId)
    {
        foreach (var item in await FindItemsAsync(connection, cameraId))
        {
            var prompt = await Call(connection, Simple(connection, item, ItemInterface, "Delete"), ReadObjectPath);
            if (prompt != NoObject && !await PromptAsync(connection, prompt)) return false;
        }
        return true;
    }

    /// <summary>The camera's items, unlocked (with the system prompt when needed). Empty when none or still locked.</summary>
    static async Task<string[]> FindItemsAsync(Connection connection, Guid cameraId)
    {
        var (unlocked, locked) = await Call(connection, SearchItems(connection, cameraId), ReadTwoPathArrays);
        if (locked.Length == 0) return unlocked;
        return await UnlockAsync(connection, locked) ? [.. unlocked, .. locked] : unlocked;
    }

    /// <summary>Unlocks the objects (no prompt when they already are). False when the user dismissed the prompt.</summary>
    static async Task<bool> UnlockAsync(Connection connection, string[] objects)
    {
        var (_, prompt) = await Call(connection, Unlock(connection, objects), ReadPathArrayAndPath);
        return prompt == NoObject || await PromptAsync(connection, prompt);
    }

    /// <summary>Shows a Secret Service prompt and waits for its Completed signal. True when it was not dismissed.</summary>
    static async Task<bool> PromptAsync(Connection connection, string prompt)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = new MatchRule { Type = MessageType.Signal, Path = prompt, Interface = PromptInterface, Member = "Completed" };
        // Subscribed before Prompt is called, so the signal cannot be missed.
        using var subscription = await connection.AddMatchAsync(rule,
            static (Message message, object? _) => message.GetBodyReader().ReadBool(),
            (Exception? error, bool dismissed, object? _, object? _) => completed.TrySetResult(error is null && !dismissed),
            ObserverFlags.None, readerState: null, handlerState: null, emitOnCapturedContext: false)
            .AsTask().WaitAsync(SessionBus.CallTimeout);
        // Window id "": the prompt is not tied to one of our windows.
        await connection.CallMethodAsync(Simple(connection, prompt, PromptInterface, "Prompt", "s", "")).WaitAsync(SessionBus.CallTimeout);
        try
        {
            return await completed.Task.WaitAsync(PromptTimeout);
        }
        catch (TimeoutException)
        {
            await DismissAsync(connection, prompt);
            return false;
        }
    }

    static async Task DismissAsync(Connection connection, string prompt)
    {
        try
        {
            await connection.CallMethodAsync(Simple(connection, prompt, PromptInterface, "Dismiss")).WaitAsync(SessionBus.CallTimeout);
        }
        catch (Exception)
        {
            // Best effort: the prompt may already be gone.
        }
    }

    static Task<string> OpenSessionAsync(Connection connection) =>
        Call(connection, OpenSession(connection), static (Message message, object? _) =>
        {
            var reader = message.GetBodyReader();
            reader.ReadVariantValue(); // "plain" has no output
            return reader.ReadObjectPathAsString();
        });

    static async Task CloseSessionAsync(Connection connection, string session)
    {
        try
        {
            await connection.CallMethodAsync(Simple(connection, session, "org.freedesktop.Secret.Session", "Close"))
                .WaitAsync(SessionBus.CallTimeout);
        }
        catch (Exception)
        {
            // Best effort: the service drops it with our connection anyway.
        }
    }

    static Task<T> Call<T>(Connection connection, MessageBuffer message, MessageValueReader<T> reader) =>
        connection.CallMethodAsync(message, reader).WaitAsync(SessionBus.CallTimeout);

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
