using System.Text.Json;
using System.Text.Json.Serialization;

namespace Native.IdentityCore.Serialization;

/// <summary>
/// (De)serializes a JWT <c>aud</c> claim (RFC 7519 §4.1.3): a single JSON string when there is
/// exactly one audience, a JSON array of strings otherwise. Manual, allocation-light, no
/// reflection — safe for the source-generated <see cref="JsonSerializerContext"/> (GS-02).
/// </summary>
public sealed class AudienceJsonConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return value is null ? [] : [value];
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var audience = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("The \"aud\" claim array must contain only strings.");
                }

                if (reader.GetString() is { } item)
                {
                    audience.Add(item);
                }
            }

            return audience;
        }

        throw new JsonException("The \"aud\" claim must be a JSON string or an array of strings.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Count == 1)
        {
            writer.WriteStringValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var audience in value)
        {
            writer.WriteStringValue(audience);
        }

        writer.WriteEndArray();
    }
}
