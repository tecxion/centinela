using Tmds.DBus.Protocol;

namespace Centinela.Platform.Linux;

/// <summary>The Secret Service's replies (read) and requests (built) for <see cref="SecretServiceClient"/>.</summary>
internal sealed partial class SecretServiceClient
{
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
