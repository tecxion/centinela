using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centinela.Core;

/// <summary>
/// Reads an enum written as a name (any case) or a number; an unknown value or null becomes <paramref name="fallback"/>
/// instead of failing the whole file. Writes the name.
/// </summary>
abstract class LenientEnumConverter<T>(T fallback) : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var v) && Enum.IsDefined(v) ? v : fallback,
            JsonTokenType.Number => reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), n) ? (T)Enum.ToObject(typeof(T), n) : fallback,
            JsonTokenType.Null => fallback,
            _ => throw new JsonException($"Invalid {typeof(T).Name}."),
        };

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Unknown brand names or numbers map to <see cref="Brand.Custom"/>.</summary>
sealed class LenientBrandConverter() : LenientEnumConverter<Brand>(Brand.Custom);

/// <summary>Unknown sensitivity names or numbers map to <see cref="MotionSensitivity.Medium"/>.</summary>
sealed class LenientSensitivityConverter() : LenientEnumConverter<MotionSensitivity>(MotionSensitivity.Medium);
